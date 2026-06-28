import { useState, useEffect, useRef } from 'preact/hooks';
import { t } from '../i18n';

interface TelemetryKeyDto {
    key: string;
    path: string;
    units?: string;
}

interface DataPoint {
    ts: number;
    value: number | null;
    valueStr: string | null;
}

export function TelemetryCrudPage({ initialKey }: { initialKey?: string | null } = {}) {
    const [keys, setKeys] = useState<TelemetryKeyDto[]>([]);
    const [selectedKey, setSelectedKey] = useState<string>(initialKey || '');
    const [searchQuery, setSearchQuery] = useState<string>('');
    
    // Querying state
    const [startQueryDate, setStartQueryDate] = useState<string>(() => {
        const d = new Date();
        d.setDate(d.getDate() - 7); // Default 7 days ago
        return d.toISOString().slice(0, 16);
    });
    const [endQueryDate, setEndQueryDate] = useState<string>(() => {
        return new Date().toISOString().slice(0, 16);
    });
    const [queriedData, setQueriedData] = useState<DataPoint[]>([]);
    const [queryLimit, setQueryLimit] = useState<number>(500);
    const [isQuerying, setIsQuerying] = useState<boolean>(false);
    const [queryError, setQueryError] = useState<string>('');

    // Manual insert state
    const [manualDate, setManualDate] = useState<string>(() => new Date().toISOString().slice(0, 16));
    const [manualValue, setManualValue] = useState<string>('');
    const [manualIsString, setManualIsString] = useState<boolean>(false);
    const [insertSuccess, setInsertSuccess] = useState<string>('');
    const [insertError, setInsertError] = useState<string>('');

    // Delete range state
    const [deleteStartDate, setDeleteStartDate] = useState<string>(() => {
        const d = new Date();
        d.setDate(d.getDate() - 30);
        return d.toISOString().slice(0, 16);
    });
    const [deleteEndDate, setDeleteEndDate] = useState<string>(() => new Date().toISOString().slice(0, 16));
    const [deleteSuccess, setDeleteSuccess] = useState<string>('');
    const [deleteError, setDeleteError] = useState<string>('');

    // CSV Import state
    const [csvFile, setCsvFile] = useState<File | null>(null);
    const [parsedPoints, setParsedPoints] = useState<{ ts: number; value: any }[]>([]);
    const [csvError, setCsvError] = useState<string>('');
    const [csvSuccess, setCsvSuccess] = useState<string>('');
    const [isUploading, setIsUploading] = useState<boolean>(false);
    const [uploadProgress, setUploadProgress] = useState<number>(0);
    const [uploadCount, setUploadCount] = useState<number>(0);
    const [hasHeader, setHasHeader] = useState<boolean>(true);
    const [tsColumnIdx, setTsColumnIdx] = useState<number>(0);
    const [valColumnIdx, setValColumnIdx] = useState<number>(1);
    const [columns, setColumns] = useState<string[]>([]);
    const [rawRows, setRawRows] = useState<string[][]>([]);

    const fileInputRef = useRef<HTMLInputElement>(null);

    // Fetch telemetry keys on mount
    useEffect(() => {
        const fetchKeys = async () => {
            try {
                const res = await fetch('/plswk/api/telemetry-keys');
                if (res.ok) {
                    const data = await res.json();
                    setKeys(data);
                    // Prefer a deep-linked key (?key=...) if it exists in the list,
                    // otherwise fall back to the first available key.
                    const wanted = initialKey && data.some((k: TelemetryKeyDto) => k.key === initialKey)
                        ? initialKey
                        : (data.length > 0 ? data[0].key : '');
                    if (wanted) {
                        setSelectedKey(wanted);
                    }
                }
            } catch (e) {
                console.error("Failed to load telemetry keys", e);
            }
        };
        fetchKeys();
    }, []);

    // Filter telemetry keys based on query
    const filteredKeys = keys.filter(k => 
        k.key.toLowerCase().includes(searchQuery.toLowerCase()) || 
        k.path.toLowerCase().includes(searchQuery.toLowerCase())
    );

    // Keep selected key updated if list filters and selected key is no longer in filtered list
    useEffect(() => {
        if (filteredKeys.length > 0 && !filteredKeys.some(k => k.key === selectedKey)) {
            setSelectedKey(filteredKeys[0].key);
        }
    }, [searchQuery, keys]);

    // Fetch existing data points
    const handleQuery = async () => {
        if (!selectedKey) {
            setQueryError(t('crud_err_select_key') || 'Please select a telemetry key');
            return;
        }

        setIsQuerying(true);
        setQueryError('');
        try {
            const startMs = new Date(startQueryDate).getTime();
            const endMs = new Date(endQueryDate).getTime();
            const res = await fetch(`/plswk/api/telemetry/data?key=${encodeURIComponent(selectedKey)}&startTs=${startMs}&endTs=${endMs}&limit=${queryLimit}`);
            if (res.ok) {
                const data = await res.json();
                setQueriedData(data);
            } else {
                setQueryError(`Server returned status: ${res.statusText}`);
            }
        } catch (e: any) {
            setQueryError(e.message || "Failed to query historical data");
        } finally {
            setIsQuerying(false);
        }
    };

    // Manual single point insert
    const handleManualInsert = async (e: Event) => {
        e.preventDefault();
        setInsertSuccess('');
        setInsertError('');

        if (!selectedKey) {
            setInsertError(t('crud_err_select_key') || 'Please select a telemetry key');
            return;
        }
        if (manualValue.trim() === '') {
            setInsertError(t('crud_err_val_required') || 'Value is required');
            return;
        }

        try {
            const tsMs = new Date(manualDate).getTime();
            let valueToSend: any = manualValue;

            if (!manualIsString) {
                // Support comma separator manually entered
                let normalizedVal = manualValue.trim();
                if (normalizedVal.includes(',') && !normalizedVal.includes('.')) {
                    normalizedVal = normalizedVal.replace(',', '.');
                }
                const num = parseFloat(normalizedVal);
                if (isNaN(num)) {
                    setInsertError(t('crud_err_invalid_number') || 'Value must be a valid number');
                    return;
                }
                valueToSend = num;
            }

            const res = await fetch('/plswk/api/telemetry/data', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    key: selectedKey,
                    ts: tsMs,
                    value: valueToSend
                })
            });

            if (res.ok) {
                setInsertSuccess(t('crud_msg_insert_success') || 'Data point inserted successfully!');
                setManualValue('');
                handleQuery(); // Refresh list
            } else {
                const errText = await res.text();
                setInsertError(errText || 'Failed to insert data point.');
            }
        } catch (e: any) {
            setInsertError(e.message || 'Error communicating with server.');
        }
    };

    // Delete range of points
    const handleDeleteRange = async () => {
        setDeleteSuccess('');
        setDeleteError('');

        if (!selectedKey) {
            setDeleteError(t('crud_err_select_key') || 'Please select a telemetry key');
            return;
        }

        const startMs = new Date(deleteStartDate).getTime();
        const endMs = new Date(deleteEndDate).getTime();

        const template = t('crud_confirm_delete_range') || 'Are you sure you want to delete data points for {key} from {start} to {end}?';
        const confirmMsg = template
            .replace('{key}', selectedKey)
            .replace('{start}', deleteStartDate)
            .replace('{end}', deleteEndDate);

        if (!confirm(confirmMsg)) return;

        try {
            const res = await fetch(`/plswk/api/telemetry/data?key=${encodeURIComponent(selectedKey)}&startTs=${startMs}&endTs=${endMs}`, {
                method: 'DELETE'
            });

            if (res.ok) {
                setDeleteSuccess(t('crud_msg_delete_success') || 'Range deleted successfully!');
                handleQuery(); // Refresh list
            } else {
                const errText = await res.text();
                setDeleteError(errText || 'Failed to delete range.');
            }
        } catch (e: any) {
            setDeleteError(e.message || 'Error communicating with server.');
        }
    };

    // Delete single point from list table
    const handleDeleteSingle = async (ts: number) => {
        if (!confirm(t('crud_confirm_delete_point') || 'Delete this data point?')) return;

        try {
            const res = await fetch(`/plswk/api/telemetry/data?key=${encodeURIComponent(selectedKey)}&startTs=${ts}&endTs=${ts}`, {
                method: 'DELETE'
            });

            if (res.ok) {
                handleQuery(); // Refresh list
            } else {
                alert('Failed to delete data point.');
            }
        } catch (e) {
            console.error(e);
            alert('Error deleting data point.');
        }
    };

    // CSV Parse Logic
    const parseCSV = (text: string) => {
        setCsvError('');
        setCsvSuccess('');
        try {
            const lines = text.split(/\r?\n/).map(line => line.trim()).filter(line => line.length > 0);
            if (lines.length === 0) {
                setCsvError(t('crud_err_empty_csv') || 'CSV content is empty.');
                return;
            }

            // Split all lines by comma or semicolon
            const separator = text.includes(';') ? ';' : ',';
            const parsedRows = lines.map(line => {
                // simple CSV column extraction (does not support nested commas in quotes, which is fine for normal telemetry CSVs)
                return line.split(separator).map(col => {
                    col = col.trim();
                    if (col.startsWith('"') && col.endsWith('"')) {
                        col = col.slice(1, -1);
                    }
                    return col;
                });
            });

            setRawRows(parsedRows);

            if (hasHeader) {
                setColumns(parsedRows[0]);
            } else {
                const maxCols = Math.max(...parsedRows.map(r => r.length));
                const cols = [];
                for (let i = 0; i < maxCols; i++) cols.push(`Column ${i + 1}`);
                setColumns(cols);
            }
        } catch (e: any) {
            setCsvError(e.message || 'Failed to parse CSV file.');
        }
    };

    // Parse specific columns from CSV rows to telemetry points
    useEffect(() => {
        if (rawRows.length === 0) return;

        const dataRows = hasHeader ? rawRows.slice(1) : rawRows;
        const pts: { ts: number; value: any }[] = [];
        let parseFailures = 0;

        for (let i = 0; i < dataRows.length; i++) {
            const row = dataRows[i];
            if (row.length <= Math.max(tsColumnIdx, valColumnIdx)) continue;

            const rawTs = row[tsColumnIdx];
            const rawVal = row[valColumnIdx];

            // Parse timestamp
            let tsMs = 0;
            if (/^\d+$/.test(rawTs)) {
                // If it is pure integer, check length
                const parsedInt = parseInt(rawTs, 10);
                if (rawTs.length === 10) {
                    tsMs = parsedInt * 1000; // UNIX seconds
                } else {
                    tsMs = parsedInt; // UNIX milliseconds or default
                }
            } else {
                // Try parsing standard datetime string
                const parsedDate = new Date(rawTs);
                tsMs = parsedDate.getTime();
            }

            // Parse value (handling locality: comma vs dot decimal points)
            let valObj: any = rawVal;
            let valNormalized = rawVal.trim();
            
            if (valNormalized.includes(',') && !valNormalized.includes('.')) {
                // E.g. "23,5" or "123,45" -> replace comma with dot
                valNormalized = valNormalized.replace(',', '.');
            } else if (valNormalized.includes(',') && valNormalized.includes('.')) {
                // If it contains both (e.g. "1,234.56" or "1.234,56")
                if (valNormalized.indexOf(',') > valNormalized.indexOf('.')) {
                    // "1.234,56" -> European style, remove dot, replace comma with dot
                    valNormalized = valNormalized.replace(/\./g, '').replace(',', '.');
                } else {
                    // "1,234.56" -> US style, remove comma
                    valNormalized = valNormalized.replace(/,/g, '');
                }
            }

            const parsedFloat = parseFloat(valNormalized);
            if (!isNaN(parsedFloat)) {
                valObj = parsedFloat;
            } else if (rawVal.toLowerCase() === 'true') {
                valObj = 1.0;
            } else if (rawVal.toLowerCase() === 'false') {
                valObj = 0.0;
            }

            if (!isNaN(tsMs) && tsMs > 0 && rawVal !== undefined) {
                pts.push({ ts: tsMs, value: valObj });
            } else {
                parseFailures++;
            }
        }

        setParsedPoints(pts);

        if (pts.length === 0) {
            setCsvError(t('crud_err_no_valid_rows') || 'Could not parse any valid timestamp/value pairs.');
        } else if (parseFailures > 0) {
            const template = t('crud_msg_parsed_with_warnings') || 'Parsed {parsed} rows ({failed} skipped due to invalid formats).';
            const msg = template
                .replace('{parsed}', pts.length.toString())
                .replace('{failed}', parseFailures.toString());
            setCsvSuccess(msg);
        } else {
            const template = t('crud_msg_parsed_success') || 'Successfully parsed {count} rows.';
            const msg = template.replace('{count}', pts.length.toString());
            setCsvSuccess(msg);
        }
    }, [rawRows, hasHeader, tsColumnIdx, valColumnIdx]);

    // Handle CSV file selection
    const handleFileChange = (e: Event) => {
        const input = e.target as HTMLInputElement;
        if (input.files && input.files[0]) {
            const file = input.files[0];
            setCsvFile(file);

            const reader = new FileReader();
            reader.onload = (event) => {
                const text = event.target?.result as string;
                parseCSV(text);
            };
            reader.readAsText(file);
        }
    };

    // Bulk upload parsed CSV data in chunks
    const handleBulkUpload = async () => {
        if (!selectedKey) {
            setCsvError(t('crud_err_select_key') || 'Please select a telemetry key');
            return;
        }
        if (parsedPoints.length === 0) {
            setCsvError(t('crud_err_no_data_to_upload') || 'No data parsed to upload.');
            return;
        }

        setIsUploading(true);
        setUploadProgress(0);
        setUploadCount(0);
        setCsvError('');

        const chunkSize = 500;
        const total = parsedPoints.length;
        let successCount = 0;

        for (let i = 0; i < total; i += chunkSize) {
            const chunk = parsedPoints.slice(i, i + chunkSize);
            try {
                const res = await fetch('/plswk/api/telemetry/data/batch', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({
                        key: selectedKey,
                        points: chunk
                    })
                });

                if (res.ok) {
                    successCount += chunk.length;
                    setUploadCount(successCount);
                    setUploadProgress(Math.round((successCount / total) * 100));
                } else {
                    const errMsg = await res.text();
                    throw new Error(errMsg || `Chunk failed with status ${res.status}`);
                }
            } catch (e: any) {
                const template = t('crud_err_upload_failed') || 'Upload failed at row {success} of {total}.';
                const msg = template
                    .replace('{success}', successCount.toString())
                    .replace('{total}', total.toString());
                setCsvError(`${msg} Error: ${e.message}`);
                setIsUploading(false);
                handleQuery(); // Refresh queried table
                return;
            }
        }

        const template = t('crud_msg_upload_complete') || 'Import completed! Successfully uploaded {count} data points.';
        const msg = template.replace('{count}', successCount.toString());
        setCsvSuccess(msg);
        setIsUploading(false);
        setCsvFile(null);
        setParsedPoints([]);
        setRawRows([]);
        if (fileInputRef.current) fileInputRef.current.value = '';
        handleQuery(); // Refresh queried table
    };

    const formatDate = (ts: number): string => {
        const d = new Date(ts);
        return d.toLocaleString();
    };

    return (
        <div class="flex flex-col gap-6 w-full page-enter">
            {/* Header section with telemetry key selector */}
            <div class="glass p-6 rounded-2xl border border-white/5 flex flex-col md:flex-row gap-6 items-end">
                <div class="flex-1 w-full flex flex-col gap-2">
                    <label class="text-xs font-semibold text-slate-400 uppercase tracking-wider">{t('crud_select_telemetry_key') || 'Select Telemetry Key'}</label>
                    <div class="relative w-full">
                        <select 
                            class="form-select w-full"
                            value={selectedKey}
                            onChange={(e) => setSelectedKey((e.target as HTMLSelectElement).value)}
                        >
                            {filteredKeys.map(k => (
                                <option key={k.key} value={k.key}>{k.path} ({k.key})</option>
                            ))}
                        </select>
                    </div>
                </div>

                <div class="w-full md:w-80 flex flex-col gap-2">
                    <label class="text-xs font-semibold text-slate-400 uppercase tracking-wider">{t('crud_filter_keys') || 'Search Keys'}</label>
                    <input 
                        type="text" 
                        class="form-input"
                        placeholder="Search key or path..."
                        value={searchQuery}
                        onInput={(e) => setSearchQuery((e.target as HTMLInputElement).value)}
                    />
                </div>
            </div>

            <div class="grid grid-cols-1 lg:grid-cols-12 gap-6 items-start">
                
                {/* Left side actions: Manual Insertion & Range Deletion */}
                <div class="lg:col-span-5 flex flex-col gap-6 w-full">
                    
                    {/* Manual entry Card */}
                    <div class="glass p-6 rounded-2xl border border-white/5 flex flex-col gap-4">
                        <h3 class="text-base font-bold text-white flex items-center gap-2">
                            <i class="fas fa-plus text-cyan-400"></i>
                            {t('crud_add_datapoint') || 'Add Data Point'}
                        </h3>
                        
                        <form onSubmit={handleManualInsert} class="flex flex-col gap-4">
                            <div class="flex flex-col gap-1.5">
                                <label class="text-[0.7rem] uppercase tracking-wider font-bold text-slate-400">{t('crud_timestamp') || 'Timestamp'}</label>
                                <input 
                                    type="datetime-local" 
                                    class="form-input" 
                                    value={manualDate}
                                    onChange={(e) => setManualDate((e.target as HTMLInputElement).value)}
                                    step="1"
                                />
                            </div>

                            <div class="flex flex-col gap-1.5">
                                <label class="text-[0.7rem] uppercase tracking-wider font-bold text-slate-400">{t('crud_value') || 'Value'}</label>
                                <input 
                                    type="text" 
                                    class="form-input font-mono" 
                                    placeholder="e.g. 23.5 or Active"
                                    value={manualValue}
                                    onInput={(e) => setManualValue((e.target as HTMLInputElement).value)}
                                />
                            </div>

                            <div class="flex items-center gap-2 py-1">
                                <input 
                                    type="checkbox" 
                                    id="manualIsString"
                                    class="w-4 h-4 rounded bg-slate-900 border-slate-700 text-cyan-400 focus:ring-cyan-400 cursor-pointer"
                                    checked={manualIsString}
                                    onChange={(e) => setManualIsString((e.target as HTMLInputElement).checked)}
                                />
                                <label for="manualIsString" class="text-xs text-slate-300 font-semibold cursor-pointer">
                                    {t('crud_store_as_string') || 'Store as text/string value'}
                                </label>
                            </div>

                            {insertSuccess && <div class="text-xs bg-emerald-500/10 text-emerald-400 border border-emerald-500/20 px-3 py-2 rounded-lg">{insertSuccess}</div>}
                            {insertError && <div class="text-xs bg-red-500/10 text-red-400 border border-red-500/20 px-3 py-2 rounded-lg">{insertError}</div>}

                            <button type="submit" class="btn-primary w-full bg-cyan-400 hover:bg-cyan-500 text-slate-900 font-bold py-2.5 rounded-xl text-sm justify-center">
                                <i class="fas fa-save"></i>
                                {t('crud_btn_insert') || 'Insert Record'}
                            </button>
                        </form>
                    </div>

                    {/* Delete range Card */}
                    <div class="glass p-6 rounded-2xl border border-white/5 flex flex-col gap-4">
                        <h3 class="text-base font-bold text-white flex items-center gap-2">
                            <i class="fas fa-trash-alt text-red-400"></i>
                            {t('crud_delete_range') || 'Delete Range'}
                        </h3>
                        
                        <div class="flex flex-col gap-3">
                            <div class="grid grid-cols-2 gap-3">
                                <div class="flex flex-col gap-1.5">
                                    <label class="text-[0.7rem] uppercase tracking-wider font-bold text-slate-400">{t('crud_start') || 'Start'}</label>
                                    <input 
                                        type="datetime-local" 
                                        class="form-input" 
                                        value={deleteStartDate}
                                        onChange={(e) => setDeleteStartDate((e.target as HTMLInputElement).value)}
                                    />
                                </div>
                                <div class="flex flex-col gap-1.5">
                                    <label class="text-[0.7rem] uppercase tracking-wider font-bold text-slate-400">{t('crud_end') || 'End'}</label>
                                    <input 
                                        type="datetime-local" 
                                        class="form-input" 
                                        value={deleteEndDate}
                                        onChange={(e) => setDeleteEndDate((e.target as HTMLInputElement).value)}
                                    />
                                </div>
                            </div>

                            {deleteSuccess && <div class="text-xs bg-emerald-500/10 text-emerald-400 border border-emerald-500/20 px-3 py-2 rounded-lg">{deleteSuccess}</div>}
                            {deleteError && <div class="text-xs bg-red-500/10 text-red-400 border border-red-500/20 px-3 py-2 rounded-lg">{deleteError}</div>}

                            <button 
                                onClick={handleDeleteRange}
                                class="btn-primary w-full bg-red-500/10 border border-red-500/30 hover:bg-red-500/20 text-red-400 font-bold py-2.5 rounded-xl text-sm justify-center"
                            >
                                <i class="fas fa-trash"></i>
                                {t('crud_btn_delete_range') || 'Delete Selected Range'}
                            </button>
                        </div>
                    </div>
                </div>

                {/* Right side CSV Importer Card */}
                <div class="lg:col-span-7 w-full">
                    <div class="glass p-6 rounded-2xl border border-white/5 flex flex-col gap-4 min-h-[460px]">
                        <h3 class="text-base font-bold text-white flex items-center gap-2">
                            <i class="fas fa-file-csv text-cyan-400"></i>
                            {t('crud_import_csv') || 'Bulk CSV Import'}
                        </h3>

                        <div class="flex flex-col gap-4">
                            {/* Drag and Drop zone */}
                            <div 
                                onClick={() => fileInputRef.current?.click()}
                                class="border-2 border-dashed border-slate-700 hover:border-cyan-400/50 hover:bg-cyan-400/5 rounded-2xl p-8 text-center cursor-pointer transition-all duration-200"
                            >
                                <input 
                                    ref={fileInputRef}
                                    type="file" 
                                    accept=".csv,.txt"
                                    class="hidden"
                                    onChange={handleFileChange}
                                />
                                <i class="fas fa-cloud-upload-alt text-slate-500 text-3xl mb-2"></i>
                                <p class="text-sm font-semibold text-slate-300">
                                    {csvFile ? csvFile.name : (t('crud_drag_csv_here') || 'Drag & Drop CSV file, or click to select')}
                                </p>
                                <p class="text-xs text-slate-500 mt-1">Supports Timestamp, Value mappings</p>
                            </div>

                            {/* Parsing Options */}
                            {rawRows.length > 0 && (
                                <div class="p-4 bg-slate-900/60 border border-slate-800 rounded-xl flex flex-col gap-4">
                                    <div class="flex items-center gap-4">
                                        <div class="flex items-center gap-2">
                                            <input 
                                                type="checkbox" 
                                                id="hasHeader"
                                                class="w-4 h-4 rounded bg-slate-950 border-slate-700 text-cyan-400 focus:ring-cyan-400"
                                                checked={hasHeader}
                                                onChange={(e) => setHasHeader((e.target as HTMLInputElement).checked)}
                                            />
                                            <label for="hasHeader" class="text-xs text-slate-300 font-semibold cursor-pointer">
                                                {t('crud_csv_has_header') || 'CSV has headers'}
                                            </label>
                                        </div>
                                    </div>

                                    <div class="grid grid-cols-2 gap-4">
                                        <div class="flex flex-col gap-1.5">
                                            <label class="text-[0.7rem] uppercase tracking-wider font-bold text-slate-400">{t('crud_col_timestamp') || 'Timestamp Column'}</label>
                                            <select 
                                                class="form-select text-xs"
                                                value={tsColumnIdx}
                                                onChange={(e) => setTsColumnIdx(parseInt((e.target as HTMLSelectElement).value, 10))}
                                            >
                                                {columns.map((col, idx) => (
                                                    <option key={idx} value={idx}>{col} (col {idx + 1})</option>
                                                ))}
                                            </select>
                                        </div>
                                        <div class="flex flex-col gap-1.5">
                                            <label class="text-[0.7rem] uppercase tracking-wider font-bold text-slate-400">{t('crud_col_value') || 'Value Column'}</label>
                                            <select 
                                                class="form-select text-xs"
                                                value={valColumnIdx}
                                                onChange={(e) => setValColumnIdx(parseInt((e.target as HTMLSelectElement).value, 10))}
                                            >
                                                {columns.map((col, idx) => (
                                                    <option key={idx} value={idx}>{col} (col {idx + 1})</option>
                                                ))}
                                            </select>
                                        </div>
                                    </div>
                                </div>
                            )}

                            {csvError && <div class="text-xs bg-red-500/10 text-red-400 border border-red-500/20 px-3 py-2 rounded-lg">{csvError}</div>}
                            {csvSuccess && <div class="text-xs bg-emerald-500/10 text-emerald-400 border border-emerald-500/20 px-3 py-2 rounded-lg">{csvSuccess}</div>}

                            {/* Upload progress */}
                            {isUploading && (
                                <div class="flex flex-col gap-2 p-3 bg-slate-900 border border-slate-800 rounded-xl">
                                    <div class="flex justify-between text-xs font-semibold text-slate-300">
                                        <span>{t('crud_uploading') || 'Uploading...'}</span>
                                        <span>{uploadCount} / {parsedPoints.length} ({uploadProgress}%)</span>
                                    </div>
                                    <div class="w-full bg-slate-800 h-2 rounded-full overflow-hidden">
                                        <div class="bg-cyan-400 h-full transition-all duration-300" style={{ width: `${uploadProgress}%` }}></div>
                                    </div>
                                </div>
                            )}

                            {/* Preview parsed values */}
                            {parsedPoints.length > 0 && !isUploading && (
                                <div class="flex flex-col gap-2">
                                    <div class="text-xs font-bold text-slate-400 uppercase tracking-wider">{t('crud_preview') || 'Data Preview'}</div>
                                    <div class="max-h-36 overflow-y-auto border border-white/5 rounded-xl bg-slate-900/40">
                                        <table class="lv-table text-xs">
                                            <thead>
                                                <tr>
                                                    <th>Row</th>
                                                    <th>Time</th>
                                                    <th class="font-mono">Value</th>
                                                </tr>
                                            </thead>
                                            <tbody>
                                                {parsedPoints.slice(0, 5).map((pt, idx) => (
                                                    <tr key={idx}>
                                                        <td class="text-slate-500">#{idx + 1}</td>
                                                        <td>{formatDate(pt.ts)}</td>
                                                        <td class="text-cyan-400 font-mono font-bold">{pt.value.toString()}</td>
                                                    </tr>
                                                ))}
                                                {parsedPoints.length > 5 && (
                                                    <tr>
                                                        <td colspan={3} class="p-2 text-center text-slate-500 font-medium italic border-none">
                                                            ...and {parsedPoints.length - 5} more rows
                                                        </td>
                                                    </tr>
                                                )}
                                            </tbody>
                                        </table>
                                    </div>

                                    <button 
                                        onClick={handleBulkUpload}
                                        class="btn-primary w-full bg-cyan-400 hover:bg-cyan-500 text-slate-900 font-bold py-3 rounded-xl text-sm justify-center mt-2 shadow-[0_0_15px_rgba(34,211,238,0.2)]"
                                    >
                                        <i class="fas fa-file-import"></i>
                                        {t('crud_btn_import') || 'Import Parsed Points'}
                                    </button>
                                </div>
                            )}
                        </div>
                    </div>
                </div>

            </div>

            {/* Bottom section: Query existing historical records table */}
            <div class="glass p-6 rounded-2xl border border-white/5 flex flex-col gap-6">
                <div class="flex flex-col md:flex-row justify-between items-start md:items-center gap-4">
                    <h3 class="text-base font-bold text-white flex items-center gap-2">
                        <i class="fas fa-list-ul text-cyan-400"></i>
                        {t('crud_view_history') || 'Query Historical Data'}
                    </h3>
                    
                    <div class="flex flex-wrap items-center gap-3 w-full md:w-auto">
                        <div class="flex items-center gap-1 bg-slate-900/60 border border-slate-700/60 rounded-xl px-3 py-1.5">
                            <label class="text-[0.7rem] uppercase tracking-wider font-bold text-slate-400 mr-2">{t('crud_start') || 'Start'}</label>
                            <input 
                                type="datetime-local" 
                                class="bg-transparent border-none outline-none text-xs text-white" 
                                value={startQueryDate}
                                onChange={(e) => setStartQueryDate((e.target as HTMLInputElement).value)}
                            />
                        </div>

                        <div class="flex items-center gap-1 bg-slate-900/60 border border-slate-700/60 rounded-xl px-3 py-1.5">
                            <label class="text-[0.7rem] uppercase tracking-wider font-bold text-slate-400 mr-2">{t('crud_end') || 'End'}</label>
                            <input 
                                type="datetime-local" 
                                class="bg-transparent border-none outline-none text-xs text-white" 
                                value={endQueryDate}
                                onChange={(e) => setEndQueryDate((e.target as HTMLInputElement).value)}
                            />
                        </div>

                        <div class="flex items-center gap-1 bg-slate-900/60 border border-slate-700/60 rounded-xl px-3 py-1.5 w-28">
                            <label class="text-[0.7rem] uppercase tracking-wider font-bold text-slate-400 mr-2">{t('crud_limit') || 'Limit'}</label>
                            <select 
                                class="bg-transparent border-none outline-none text-xs text-white cursor-pointer w-full"
                                value={queryLimit}
                                onChange={(e) => setQueryLimit(parseInt((e.target as HTMLSelectElement).value, 10))}
                            >
                                <option value={100}>100</option>
                                <option value={500}>500</option>
                                <option value={1000}>1000</option>
                                <option value={5000}>5000</option>
                            </select>
                        </div>

                        <button 
                            onClick={handleQuery}
                            disabled={isQuerying}
                            class="btn-primary bg-cyan-400 hover:bg-cyan-500 text-slate-900 font-bold px-4 py-2 rounded-xl text-xs"
                        >
                            {isQuerying ? <i class="fas fa-spinner fa-spin mr-1"></i> : <i class="fas fa-search mr-1"></i>}
                            {t('crud_btn_query') || 'Query'}
                        </button>
                    </div>
                </div>

                {queryError && <div class="text-xs bg-red-500/10 text-red-400 border border-red-500/20 px-3 py-2 rounded-lg">{queryError}</div>}

                {/* Query Result table */}
                <div class="max-h-[500px] overflow-y-auto border border-white/5 rounded-xl bg-slate-900/20">
                    <table class="lv-table text-sm">
                        <thead>
                            <tr class="sticky top-0 backdrop-blur-md bg-slate-900/80">
                                <th>#</th>
                                <th>Time</th>
                                <th class="font-mono">Value</th>
                                <th class="text-right">Actions</th>
                            </tr>
                        </thead>
                        <tbody>
                            {queriedData.length === 0 ? (
                                <tr>
                                    <td colspan={4} class="p-8 text-center text-slate-500 font-medium italic border-none">
                                        {isQuerying ? (t('loading') || 'Loading data...') : (t('crud_no_data') || 'No data points matching query range.')}
                                    </td>
                                </tr>
                            ) : (
                                queriedData.map((pt, idx) => (
                                    <tr key={pt.ts}>
                                        <td class="text-slate-500">#{idx + 1}</td>
                                        <td class="font-semibold">{formatDate(pt.ts)}</td>
                                        <td class="text-cyan-400 font-mono font-black">
                                            {pt.value !== null ? pt.value : pt.valueStr}
                                        </td>
                                        <td class="text-right">
                                            <button 
                                                onClick={() => handleDeleteSingle(pt.ts)}
                                                class="text-red-400 hover:text-red-300 bg-red-500/5 hover:bg-red-500/15 border border-red-500/10 hover:border-red-500/30 px-2 py-1 rounded-lg text-xs transition-all cursor-pointer"
                                                title="Delete this point"
                                            >
                                                <i class="fas fa-trash-alt"></i>
                                            </button>
                                        </td>
                                    </tr>
                                ))
                            )}
                        </tbody>
                    </table>
                </div>
            </div>
        </div>
    );
}
