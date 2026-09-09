// SmgwConnection.cs – Manages HTTPS and Digest Authentication sessions to SMGW
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Pulswerk.Core;

namespace Pulswerk.Drivers.Smgw
{
    /// <summary>
    /// Thread-safe manager for HTTP Digest Auth and session lifecycle on the Smart Meter Gateway.
    /// Strictly limits operations to a single active session on the hardware.
    /// </summary>
    public class SmgwConnection : IDisposable
    {
        private static readonly ConcurrentDictionary<string, SmgwConnection> _connections = new();

        private readonly ConnectionConfig _config;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly string _baseUrl;
        private readonly string _uriPath;
        private CookieContainer _cookies = new();
        private HttpClient _client;
        private string? _cachedSessionCookie;
        private string? _cachedTkn;
        private string? _lastNonce;
        private string? _lastRealm;
        private string? _lastQop;
        private string? _lastOpaque;
        private int _nonceCount = 0;

        public SmgwConnection(ConnectionConfig config)
        {
            _config = config;
            string host = config.Address ?? "192.168.1.200";
            int port = config.Port ?? 443;

            if (host.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || host.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                _baseUrl = host.Contains("/cgi-bin/") ? host : host.TrimEnd('/') + "/cgi-bin/hanservice.cgi";
            }
            else
            {
                _baseUrl = $"https://{host}:{port}/cgi-bin/hanservice.cgi";
            }

            _uriPath = new Uri(_baseUrl).PathAndQuery;
            _client = CreateHttpClient();
        }

        private HttpClient CreateHttpClient()
        {
            _cookies = new CookieContainer();
            var handler = new SocketsHttpHandler
            {
                UseCookies = true,
                CookieContainer = _cookies,
                AllowAutoRedirect = true,
                ConnectTimeout = TimeSpan.FromSeconds(10),
                PooledConnectionLifetime = TimeSpan.FromSeconds(15),
                PooledConnectionIdleTimeout = TimeSpan.FromSeconds(15),
                Credentials = !string.IsNullOrEmpty(_config.Username) && !string.IsNullOrEmpty(_config.Password)
                    ? new NetworkCredential(_config.Username, _config.Password)
                    : null
            };

            if (_config.IgnoreSslErrors)
            {
                handler.SslOptions = new SslClientAuthenticationOptions
                {
                    RemoteCertificateValidationCallback = (_, _, _, _) => true
                };
            }

            return new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
        }

        private void ResetClient()
        {
            try { _client.Dispose(); } catch { }
            _client = CreateHttpClient();
        }

        public static SmgwConnection GetOrCreate(ConnectionConfig config)
        {
            return _connections.GetOrAdd(config.Id, _ => new SmgwConnection(config));
        }

        /// <summary>
        /// Executes an action sequence under a single authenticated session.
        /// Automatically performs login, executes the action, and guarantees clean logout.
        /// Retries once with a fresh socket if a transport error occurs.
        /// </summary>
        public async Task<T> ExecuteSessionAsync<T>(Func<SmgwConnection, Task<T>> action)
        {
            await _gate.WaitAsync();
            try
            {
                for (int attempt = 1; attempt <= 2; attempt++)
                {
                    try
                    {
                        await LoginAsync();
                        try
                        {
                            return await action(this);
                        }
                        finally
                        {
                            await LogoutAsync();
                        }
                    }
                    catch (Exception ex) when (attempt == 1 && (ex is HttpRequestException || ex is System.IO.IOException))
                    {
                        Log.Debug($"[SMGW] Session attempt 1 failed with {ex.GetType().Name} ({ex.Message}), resetting client and retrying...");
                        ResetClient();
                        await Task.Delay(500);
                    }
                }
                throw new InvalidOperationException("Unreachable");
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>
        /// Fetches the live meter readings HTML for a specific meter ID (or discovered meter).
        /// Always navigates to meterform first to initialize the gateway's CGI meter context.
        /// </summary>
        public async Task<string> GetLiveMeterProfileHtmlAsync(string? targetMeterId = null)
        {
            return await ExecuteSessionAsync(async conn =>
            {
                string formHtml = await conn.PostActionAsync("meterform");
                var meters = SmgwParser.ParseMeters(formHtml);

                string mid = "1";
                if (meters.Count > 0)
                {
                    if (!string.IsNullOrEmpty(targetMeterId))
                    {
                        string normalizedTarget = targetMeterId.Replace(" ", "").ToLowerInvariant();
                        var match = meters.Find(m => m.Name.Replace(" ", "").ToLowerInvariant().Contains(normalizedTarget)
                                                  || m.Mid.Equals(targetMeterId, StringComparison.OrdinalIgnoreCase));
                        if (match != null) mid = match.Mid;
                        else mid = meters[0].Mid;
                    }
                    else
                    {
                        mid = meters[0].Mid;
                    }
                }

                return await conn.PostActionAsync("showMeterProfile", new Dictionary<string, string> { ["mid"] = mid });
            });
        }

        /// <summary>
        /// Queries firmware versions HTML.
        /// </summary>
        public async Task<string> GetSoftwareVersionsHtmlAsync()
        {
            return await ExecuteSessionAsync(async conn =>
            {
                return await conn.PostActionAsync("swversions");
            });
        }

        /// <summary>
        /// Sends a POST request with the given action name and optional parameters.
        /// </summary>
        public async Task<string> PostActionAsync(string action, Dictionary<string, string>? parameters = null)
        {
            var contentDict = new Dictionary<string, string>
            {
                ["action"] = action
            };

            if (!string.IsNullOrEmpty(_cachedTkn))
            {
                contentDict["tkn"] = _cachedTkn;
            }

            if (parameters != null)
            {
                foreach (var kvp in parameters)
                    contentDict[kvp.Key] = kvp.Value;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, _baseUrl)
            {
                Content = new FormUrlEncodedContent(contentDict)
            };

            ApplySessionHeaders(request, HttpMethod.Post, _uriPath);

            var response = await _client.SendAsync(request);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                var authHeader = response.Headers.WwwAuthenticate.ToString();
                ParseDigestChallenge(authHeader);

                using var retryReq = new HttpRequestMessage(HttpMethod.Post, _baseUrl)
                {
                    Content = new FormUrlEncodedContent(contentDict)
                };
                ApplySessionHeaders(retryReq, HttpMethod.Post, _uriPath);
                response = await _client.SendAsync(retryReq);
            }

            response.EnsureSuccessStatusCode();
            ExtractSessionCookie(response);
            string body = await response.Content.ReadAsStringAsync();
            ExtractTkn(body);
            return body;
        }

        private async Task LoginAsync()
        {
            _cachedSessionCookie = null;
            _cachedTkn = null;
            _lastNonce = null;
            _lastRealm = null;
            _lastQop = null;
            _lastOpaque = null;
            _nonceCount = 0;

            using var req = new HttpRequestMessage(HttpMethod.Get, _baseUrl);
            var resp = await _client.SendAsync(req);

            if (resp.StatusCode == HttpStatusCode.Unauthorized)
            {
                // Challenge received, extract parameters and retry with Digest Auth
                var authHeader = resp.Headers.WwwAuthenticate.ToString();
                ParseDigestChallenge(authHeader);

                using var digestReq = new HttpRequestMessage(HttpMethod.Get, _baseUrl);
                AddDigestHeader(digestReq, HttpMethod.Get, _uriPath);

                resp = await _client.SendAsync(digestReq);
            }

            ExtractSessionCookie(resp);
            string loginBody = await resp.Content.ReadAsStringAsync();
            ExtractTkn(loginBody);
        }

        private async Task LogoutAsync()
        {
            try
            {
                var logoutParams = new Dictionary<string, string> { ["action"] = "logout" };
                if (!string.IsNullOrEmpty(_cachedTkn))
                {
                    logoutParams["tkn"] = _cachedTkn;
                }

                using var req = new HttpRequestMessage(HttpMethod.Post, _baseUrl)
                {
                    Content = new FormUrlEncodedContent(logoutParams)
                };

                ApplySessionHeaders(req, HttpMethod.Post, _uriPath);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await _client.SendAsync(req, cts.Token);
            }
            catch (Exception ex)
            {
                Log.Debug($"[SMGW] Logout notice: {ex.Message}");
            }
            finally
            {
                _cachedSessionCookie = null;
                _cachedTkn = null;
                _lastNonce = null;
            }
        }

        private void ExtractTkn(string html)
        {
            if (string.IsNullOrWhiteSpace(html)) return;
            var m = Regex.Match(html, @"name=['""]tkn['""]\s+value=['""]([^'""]+)['""]", RegexOptions.IgnoreCase);
            if (!m.Success)
                m = Regex.Match(html, @"value=['""]([^'""]+)['""]\s+name=['""]tkn['""]", RegexOptions.IgnoreCase);
            if (m.Success)
                _cachedTkn = m.Groups[1].Value;
        }

        private void ExtractSessionCookie(HttpResponseMessage resp)
        {
            var uri = new Uri(_baseUrl);
            var cookies = _cookies.GetCookies(uri);
            foreach (Cookie c in cookies)
            {
                if (c.Name.Equals("session", StringComparison.OrdinalIgnoreCase))
                {
                    _cachedSessionCookie = c.Value;
                    break;
                }
            }

            if (string.IsNullOrEmpty(_cachedSessionCookie) && resp.Headers.TryGetValues("Set-Cookie", out var setCookies))
            {
                foreach (var sc in setCookies)
                {
                    var m = Regex.Match(sc, @"session=([^;,\s]+)", RegexOptions.IgnoreCase);
                    if (m.Success)
                    {
                        _cachedSessionCookie = m.Groups[1].Value;
                        _cookies.Add(uri, new Cookie("session", _cachedSessionCookie));
                        break;
                    }
                }
            }
        }

        private void ApplySessionHeaders(HttpRequestMessage request, HttpMethod method, string path)
        {
            if (!string.IsNullOrEmpty(_cachedSessionCookie))
            {
                request.Headers.TryAddWithoutValidation("Cookie", $"session={_cachedSessionCookie}");
            }

            if (!string.IsNullOrEmpty(_lastNonce))
            {
                AddDigestHeader(request, method, path);
            }
        }

        private void ParseDigestChallenge(string header)
        {
            _lastRealm = ExtractChallengeParam(header, "realm");
            _lastNonce = ExtractChallengeParam(header, "nonce");
            _lastQop = ExtractChallengeParam(header, "qop");
            _lastOpaque = ExtractChallengeParam(header, "opaque");
            _nonceCount = 0;
        }

        private static string? ExtractChallengeParam(string header, string paramName)
        {
            var m = Regex.Match(header, $@"{paramName}=(?:""([^""]+)""|([^,\s]+))", RegexOptions.IgnoreCase);
            return m.Success ? (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value) : null;
        }

        private void AddDigestHeader(HttpRequestMessage request, HttpMethod method, string uri)
        {
            if (string.IsNullOrEmpty(_config.Username) || string.IsNullOrEmpty(_config.Password) ||
                string.IsNullOrEmpty(_lastRealm) || string.IsNullOrEmpty(_lastNonce))
            {
                return;
            }

            _nonceCount++;
            string nc = _nonceCount.ToString("D8");
            string cnonce = Guid.NewGuid().ToString("N").Substring(0, 16);

            string ha1 = Md5($"{_config.Username}:{_lastRealm}:{_config.Password}");
            string ha2 = Md5($"{method.Method}:{uri}");

            string response;
            if (!string.IsNullOrEmpty(_lastQop) && _lastQop.Contains("auth"))
            {
                response = Md5($"{ha1}:{_lastNonce}:{nc}:{cnonce}:auth:{ha2}");
            }
            else
            {
                response = Md5($"{ha1}:{_lastNonce}:{ha2}");
            }

            var sb = new StringBuilder();
            sb.Append($"Digest username=\"{_config.Username}\", ");
            sb.Append($"realm=\"{_lastRealm}\", ");
            sb.Append($"nonce=\"{_lastNonce}\", ");
            sb.Append($"uri=\"{uri}\", ");
            sb.Append($"response=\"{response}\"");

            if (!string.IsNullOrEmpty(_lastOpaque))
                sb.Append($", opaque=\"{_lastOpaque}\"");

            if (!string.IsNullOrEmpty(_lastQop) && _lastQop.Contains("auth"))
            {
                sb.Append(", qop=auth");
                sb.Append($", nc={nc}");
                sb.Append($", cnonce=\"{cnonce}\"");
            }

            request.Headers.TryAddWithoutValidation("Authorization", sb.ToString());
        }

        private static string Md5(string input)
        {
            using var md5 = MD5.Create();
            byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
            var sb = new StringBuilder(hash.Length * 2);
            foreach (byte b in hash)
                sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        public void Dispose()
        {
            _client.Dispose();
            _gate.Dispose();
        }
    }
}
