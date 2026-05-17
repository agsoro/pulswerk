export class DrawioCodec {
    static async decompress(base64Str: string): Promise<string | null> {
        try {
            const binaryStr = atob(base64Str);
            const bytes = new Uint8Array(binaryStr.length);
            for (let i = 0; i < binaryStr.length; i++) bytes[i] = binaryStr.charCodeAt(i);
            const ds = new DecompressionStream('deflate-raw');
            const decompressedStream = new Response(bytes).body!.pipeThrough(ds);
            const decompressedBytes = await new Response(decompressedStream).arrayBuffer();
            const decoder = new TextDecoder();
            const inflatedStr = decoder.decode(decompressedBytes);
            return decodeURIComponent(inflatedStr);
        } catch (e) {
            console.warn('DrawioCodec decompress error:', e);
            return null;
        }
    }

    static async compress(xmlStr: string): Promise<string | null> {
        try {
            const encodedStr = encodeURIComponent(xmlStr);
            const encoder = new TextEncoder();
            const bytes = encoder.encode(encodedStr);
            const cs = new CompressionStream('deflate-raw');
            const compressedStream = new Response(bytes).body!.pipeThrough(cs);
            const compressedBytes = await new Response(compressedStream).arrayBuffer();
            const compressedArray = new Uint8Array(compressedBytes);
            let binaryStr = '';
            for (let i = 0; i < compressedArray.length; i++) binaryStr += String.fromCharCode(compressedArray[i]);
            return btoa(binaryStr);
        } catch (e) {
            console.warn('DrawioCodec compress error:', e);
            return null;
        }
    }

    static updateXmlContent(xml: string, oldId: string, newId: string): string {
        if (!xml || typeof xml !== 'string') return xml;

        const escapedOld = oldId.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
        const refRegex = new RegExp(`(id|source|target|parent)=(['"])${escapedOld}(['"])`, 'g');
        let updatedXml = xml.replace(refRegex, `$1=$2${newId}$3`);

        // Case 1: Check if it's already an <object>
        const objectRegex = new RegExp(`<object([^>]*id=['"]${newId}['"][^>]*)>`);
        const objectMatch = updatedXml.match(objectRegex);
        if (objectMatch) {
            let attrs = objectMatch[1];
            if (/name=['"][^'"]*['"]/.test(attrs)) {
                attrs = attrs.replace(/name=['"][^'"]*['"]/g, `name="${newId}"`);
            } else {
                if (attrs.endsWith('/')) {
                    attrs = attrs.slice(0, -1).trim() + ` name="${newId}" /`;
                } else {
                    attrs = attrs.trim() + ` name="${newId}"`;
                }
            }
            updatedXml = updatedXml.replace(objectMatch[0], `<object ${attrs.trim()}>`);
            return updatedXml;
        }

        // Case 2: It's an <mxCell> that needs to be wrapped in an <object>
        const cellRegex = new RegExp(`<mxCell([^>]*id=['"]${newId}['"][^>]*)>`);
        const cellMatch = updatedXml.match(cellRegex);
        if (cellMatch) {
            const fullMatchStr = cellMatch[0];
            const attrsWithSlash = cellMatch[1];
            const isSelfClosing = fullMatchStr.endsWith('/>');

            if (isSelfClosing) {
                const valueMatch = attrsWithSlash.match(/value=(['"])(.*?)\1/);
                const labelVal = valueMatch ? valueMatch[2] : '';
                let newAttrs = attrsWithSlash.replace(/\s*id=['"][^'"]*['"]/g, '').replace(/\s*value=['"][^'"]*['"]/g, '');
                if (newAttrs.endsWith('/')) newAttrs = newAttrs.slice(0, -1);
                newAttrs = newAttrs.trim();

                const replacement = `<object id="${newId}" name="${newId}" label="${labelVal}"><mxCell ${newAttrs}/></object>`;
                updatedXml = updatedXml.replace(fullMatchStr, replacement);
            } else {
                const startIndex = cellMatch.index!;
                const endIndex = updatedXml.indexOf('</mxCell>', startIndex);
                if (endIndex !== -1) {
                    const fullCellStr = updatedXml.substring(startIndex, endIndex + '</mxCell>'.length);
                    const openTagMatch = fullCellStr.match(/^<mxCell([^>]+)>/);
                    if (openTagMatch) {
                        const attrs = openTagMatch[1];
                        const innerContent = fullCellStr.substring(openTagMatch[0].length, fullCellStr.length - '</mxCell>'.length);

                        const valueMatch = attrs.match(/value=(['"])(.*?)\1/);
                        const labelVal = valueMatch ? valueMatch[2] : '';
                        const newAttrs = attrs.replace(/\s*id=['"][^'"]*['"]/g, '').replace(/\s*value=['"][^'"]*['"]/g, '').trim();

                        const replacement = `<object id="${newId}" name="${newId}" label="${labelVal}"><mxCell ${newAttrs}>${innerContent}</mxCell></object>`;
                        updatedXml = updatedXml.replace(fullCellStr, replacement);
                    }
                }
            }
        }

        return updatedXml;
    }

    static async renameCellId(contentXml: string, oldId: string, newId: string): Promise<string> {
        try {
            if (!contentXml || typeof contentXml !== 'string') return contentXml;

            if (contentXml.includes('<diagram')) {
                const diagramRegex = /(<diagram[^>]*>)([\s\S]*?)(<\/diagram>)/gi;
                let result = contentXml;
                const matches = Array.from(contentXml.matchAll(diagramRegex));

                for (const match of matches) {
                    const fullMatch = match[0];
                    const openTag = match[1];
                    const content = match[2];
                    const closeTag = match[3];

                    let newContent = content;
                    if (content.trim().startsWith('<mxGraphModel')) {
                        newContent = this.updateXmlContent(content, oldId, newId);
                    } else {
                        let uncompressed = await this.decompress(content.trim());
                        if (uncompressed) {
                            uncompressed = this.updateXmlContent(uncompressed, oldId, newId);
                            const recompressed = await this.compress(uncompressed);
                            if (recompressed) newContent = recompressed;
                        }
                    }

                    result = result.replace(fullMatch, `${openTag}${newContent}${closeTag}`);
                }
                return result;
            } else if (contentXml.trim().startsWith('<mxGraphModel')) {
                return this.updateXmlContent(contentXml, oldId, newId);
            }
            return contentXml;
        } catch (e) {
            console.warn('DrawioCodec renameCellId error:', e);
            return contentXml;
        }
    }
}
