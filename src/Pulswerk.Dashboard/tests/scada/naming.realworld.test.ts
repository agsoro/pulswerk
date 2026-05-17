import { describe, it, expect } from 'vitest';
import { DrawioCodec } from '../../src/frontend/dashboards/scada/drawio.codec';

describe('Real-World SVG Item Naming (draw.io mxfile blob integration)', () => {
    const realWorldBeforeSvg = `<svg xmlns="http://www.w3.org/2000/svg" style="background: transparent; color-scheme: light dark; width: 100%; display: block; pointer-events: none;" xmlns:xlink="http://www.w3.org/1999/xlink" version="1.1" viewBox="0 0 201 81" content="&lt;mxfile host=&quot;embed.diagrams.net&quot;&gt;&lt;diagram id=&quot;crSJdI63X0bcBljreA65&quot; name=&quot;Seite-1&quot;&gt;1ZM9b4MwEIZ/jfeAq5SuoWmydGLIbOELtmQwMpcA/fU19RGwaKVKnTpx99yX77VhPK+HkxOtercSDEt3cmD8laVpwjPuPxMZA8myLIDKaUlJCyj0BxDcEb1pCV2UiNYa1G0MS9s0UGLEhHO2j9Ou1sRTW1HBBhSlMFt60RIVbZE+L/wMulLz5GT/EiK1mJNpk04JafsV4kfGc2ctBqsecjCTeLMuoe7th+jjYA4a/E1Bui2gHh2O87q90ghFK8rJ7/2VMn5QWBvvJd4UXRtEvuoBfN9DaHAX5kYNCIBDGFZD6EgnsDWgG32KWqmWkUT9SmFC1OSJ3DF2Bd1u9ei7COAN0uB7Pfh/1iPZ/1kQ7y6P7yu2+oX58RM=&lt;/diagram&gt;&lt;/mxfile&gt;"><defs></defs><g><g data-cell-id="0"><g data-cell-id="1"><g data-cell-id="2"><g transform="translate(0.5,0.5)"><rect x="0" y="0" width="80" height="80" fill="#ffffff" stroke="#000000" pointer-events="all" style="fill: light-dark(#ffffff, var(--ge-dark-color, #121212)); stroke: light-dark(rgb(0, 0, 0), rgb(255, 255, 255));"></rect></g></g><g data-cell-id="3"><g transform="translate(0.5,0.5)"><rect x="120" y="0" width="80" height="80" fill="#ffffff" stroke="#000000" pointer-events="all" style="fill: light-dark(#ffffff, var(--ge-dark-color, #121212)); stroke: light-dark(rgb(0, 0, 0), rgb(255, 255, 255));"></rect></g></g></g></g></g></svg>`;

    const realWorldAfterSvg = `<svg xmlns="http://www.w3.org/2000/svg" style="background: transparent; color-scheme: light dark; width: 100%; display: block; pointer-events: none;" xmlns:xlink="http://www.w3.org/1999/xlink" version="1.1" viewBox="0 0 201 81" content="&lt;mxfile host=&quot;embed.diagrams.net&quot;&gt;&lt;diagram id=&quot;crSJdI63X0bcBljreA65&quot; name=&quot;Seite-1&quot;&gt;1VSxUoQwEP0aeiDOie3heTZWFNY5spA4gTAhCvj1JpcNkDmdsbCxIvvydt/u2wwJKbv5rOnAXxQDmeQpmxPymOR5RgpiPw5ZPFIUhQdaLRiSNqASn4Bgiui7YDBGRKOUNGKIwVr1PdQmwqjWaoppjZKx6kBbuAGqmspb9FUww3GK/H7Dn0G0PChnhwd/09FAxklGTpmadhA5JaTUShl/6uYSpDMv+OLznn64XRvT0JtvEtTlzfmRp5Je7E6uBJ8SKkhojDDQrb2sGvuiqDOaJVgycZtVDbR28WTXnpAjN53TyOyRjoNfRCNmsFJHX+ADtIF5VxMlz6A6MHqxFL4zskDXpp3pCGGROwyXOKS48Hatu3liDzhjCNGlXzqmXX//2LLs8Nee2XB7wVfq7j9ATl8=&lt;/diagram&gt;&lt;/mxfile&gt;"><defs></defs><g><g data-cell-id="0"><g data-cell-id="1"><g data-cell-id="leftitem"><g transform="translate(0.5,0.5)"><rect x="0" y="0" width="80" height="80" fill="#ffffff" stroke="#000000" pointer-events="all" style="fill: light-dark(#ffffff, var(--ge-dark-color, #121212)); stroke: light-dark(rgb(0, 0, 0), rgb(255, 255, 255));"></rect></g></g><g data-cell-id="rightitem"><g transform="translate(0.5,0.5)"><rect x="120" y="0" width="80" height="80" fill="#ffffff" stroke="#000000" pointer-events="all" style="fill: light-dark(#ffffff, var(--ge-dark-color, #121212)); stroke: light-dark(rgb(0, 0, 0), rgb(255, 255, 255));"></rect></g></g></g></g></g></svg>`;

    const base64Before = '1ZM9b4MwEIZ/jfeAq5SuoWmydGLIbOELtmQwMpcA/fU19RGwaKVKnTpx99yX77VhPK+HkxOtercSDEt3cmD8laVpwjPuPxMZA8myLIDKaUlJCyj0BxDcEb1pCV2UiNYa1G0MS9s0UGLEhHO2j9Ou1sRTW1HBBhSlMFt60RIVbZE+L/wMulLz5GT/EiK1mJNpk04JafsV4kfGc2ctBqsecjCTeLMuoe7th+jjYA4a/E1Bui2gHh2O87q90ghFK8rJ7/2VMn5QWBvvJd4UXRtEvuoBfN9DaHAX5kYNCIBDGFZD6EgnsDWgG32KWqmWkUT9SmFC1OSJ3DF2Bd1u9ei7COAN0uB7Pfh/1iPZ/1kQ7y6P7yu2+oX58RM=';
    const base64After = '1VSxUoQwEP0aeiDOie3heTZWFNY5spA4gTAhCvj1JpcNkDmdsbCxIvvydt/u2wwJKbv5rOnAXxQDmeQpmxPymOR5RgpiPw5ZPFIUhQdaLRiSNqASn4Bgiui7YDBGRKOUNGKIwVr1PdQmwqjWaoppjZKx6kBbuAGqmspb9FUww3GK/H7Dn0G0PChnhwd/09FAxklGTpmadhA5JaTUShl/6uYSpDMv+OLznn64XRvT0JtvEtTlzfmRp5Je7E6uBJ8SKkhojDDQrb2sGvuiqDOaJVgycZtVDbR28WTXnpAjN53TyOyRjoNfRCNmsFJHX+ADtIF5VxMlz6A6MHqxFL4zskDXpp3pCGGROwyXOKS48Hatu3liDzhjCNGlXzqmXX//2LLs8Nee2XB7wVfq7j9ATl8=';

    // Helper to simulate browser unescaping of the content attribute
    function extractMxfileBlob(svgStr: string): string {
        const match = svgStr.match(/content="([^"]+)"/);
        if (!match) return '';
        return match[1]
            .replace(/&lt;/g, '<')
            .replace(/&gt;/g, '>')
            .replace(/&quot;/g, '"')
            .replace(/&amp;/g, '&');
    }

    it('decompresses the real-world before base64 blob to verify initial mxCell structure', async () => {
        const decompressed = await DrawioCodec.decompress(base64Before);
        expect(decompressed).toBeDefined();
        expect(decompressed).toContain('<mxCell id="2"');
        expect(decompressed).toContain('<mxCell id="3"');
        expect(decompressed).not.toContain('leftitem');
        expect(decompressed).not.terms = undefined;
    });

    it('decompresses the real-world after base64 blob to verify draw.io expected object structure', async () => {
        const decompressed = await DrawioCodec.decompress(base64After);
        expect(decompressed).toBeDefined();
        expect(decompressed).toContain('<object label="" id="leftitem">');
        expect(decompressed).toContain('<object label="" id="rightitem">');
    });

    it('checks and updates the embedded mxfile blob directly, matching the real-world example perfectly', async () => {
        // Extract the mxfile blob from the before SVG
        const mxfileBlobBefore = extractMxfileBlob(realWorldBeforeSvg);
        expect(mxfileBlobBefore).toContain('<mxfile host="embed.diagrams.net">');
        expect(mxfileBlobBefore).toContain('<diagram id="crSJdI63X0bcBljreA65" name="Seite-1">');

        // Step 1: Rename cell 2 -> leftitem in the mxfile blob
        const mxfileBlobStep1 = await DrawioCodec.renameCellId(mxfileBlobBefore, '2', 'leftitem');
        expect(mxfileBlobStep1).toContain('<mxfile host="embed.diagrams.net">');

        // Step 2: Rename cell 3 -> rightitem in the mxfile blob
        const mxfileBlobFinal = await DrawioCodec.renameCellId(mxfileBlobStep1, '3', 'rightitem');
        expect(mxfileBlobFinal).toContain('<mxfile host="embed.diagrams.net">');
        expect(mxfileBlobFinal).toContain('<diagram id="crSJdI63X0bcBljreA65" name="Seite-1">');

        // Extract the newly compressed base64 blob from diagram tag of the updated mxfile blob
        const match = mxfileBlobFinal.match(/<diagram[^>]*>([^<]+)<\/diagram>/);
        expect(match).toBeDefined();
        const newBase64Blob = match![1];

        // Decompress the new base64 blob to prove the draw.io XML inside the mxfile blob has been perfectly updated
        const decompressedFinal = await DrawioCodec.decompress(newBase64Blob);
        expect(decompressedFinal).toBeDefined();

        expect(decompressedFinal).toContain('<object id="leftitem" name="leftitem"');
        expect(decompressedFinal).toContain('<object id="rightitem" name="rightitem"');
        expect(decompressedFinal).not.toContain('<mxCell id="2"');
        expect(decompressedFinal).not.toContain('<mxCell id="3"');
    });

    it('simulates full SCADA dashboard SVG outer HTML + mxfile blob update workflow', async () => {
        let currentSvgHtml = realWorldBeforeSvg;

        // 1. Extract the mxfile blob
        let mxfileBlob = extractMxfileBlob(currentSvgHtml);

        // 2. Simulate renaming cell 2 -> leftitem (both DOM attribute and mxfile blob)
        currentSvgHtml = currentSvgHtml.replace('data-cell-id="2"', 'data-cell-id="leftitem"');
        mxfileBlob = await DrawioCodec.renameCellId(mxfileBlob, '2', 'leftitem');

        // 3. Simulate renaming cell 3 -> rightitem (both DOM attribute and mxfile blob)
        currentSvgHtml = currentSvgHtml.replace('data-cell-id="3"', 'data-cell-id="rightitem"');
        mxfileBlob = await DrawioCodec.renameCellId(mxfileBlob, '3', 'rightitem');

        // 4. Re-embed the updated mxfile blob into the content attribute (escaping entities as browser serializer would)
        const escapedMxfileBlob = mxfileBlob
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;');
        
        currentSvgHtml = currentSvgHtml.replace(/content="[^"]+"/, `content="${escapedMxfileBlob}"`);

        // ─── VERIFY 1: THE SVG OUTER HTML CHANGES ─────────────────────────────────
        expect(currentSvgHtml).toContain('data-cell-id="leftitem"');
        expect(currentSvgHtml).toContain('data-cell-id="rightitem"');
        expect(currentSvgHtml).not.toContain('data-cell-id="2"');
        expect(currentSvgHtml).not.toContain('data-cell-id="3"');

        // ─── VERIFY 2: THE MXFILE BLOB CHANGES ────────────────────────────────────
        expect(currentSvgHtml).toContain('&lt;mxfile host=&quot;embed.diagrams.net&quot;&gt;');
        expect(currentSvgHtml).toContain('&lt;diagram id=&quot;crSJdI63X0bcBljreA65&quot; name=&quot;Seite-1&quot;&gt;');

        // Extract the final mxfile blob from the updated SVG HTML and decompress it to prove the inner XML is perfectly updated
        const finalMxfileBlob = extractMxfileBlob(currentSvgHtml);
        const match = finalMxfileBlob.match(/<diagram[^>]*>([^<]+)<\/diagram>/);
        expect(match).toBeDefined();

        const decompressedFinalBlob = await DrawioCodec.decompress(match![1]);
        expect(decompressedFinalBlob).toContain('<object id="leftitem" name="leftitem"');
        expect(decompressedFinalBlob).toContain('<object id="rightitem" name="rightitem"');
        expect(decompressedFinalBlob).not.toContain('<mxCell id="2"');
        expect(decompressedFinalBlob).not.toContain('<mxCell id="3"');
    });
});
