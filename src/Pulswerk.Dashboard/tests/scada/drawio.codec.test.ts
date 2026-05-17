import { describe, it, expect } from 'vitest';
import { DrawioCodec } from '../../src/frontend/dashboards/scada/drawio.codec';

describe('DrawioCodec', () => {
    it('compresses and decompresses XML perfectly', async () => {
        const originalXml = '<mxGraphModel><root><mxCell id="test1"/></root></mxGraphModel>';
        const base64 = await DrawioCodec.compress(originalXml);
        expect(base64).toBeDefined();
        expect(typeof base64).toBe('string');
        
        const decompressed = await DrawioCodec.decompress(base64!);
        expect(decompressed).toBe(originalXml);
    });

    it('renames cell IDs in raw mxGraphModel XML correctly', async () => {
        const rawXml = '<mxGraphModel><root><mxCell id="oldId" source="oldId" target="oldId" parent="oldId"/></root></mxGraphModel>';
        const renamedXml = await DrawioCodec.renameCellId(rawXml, 'oldId', 'newId');
        expect(renamedXml).toContain('id="newId"');
        expect(renamedXml).toContain('source="newId"');
        expect(renamedXml).toContain('target="newId"');
        expect(renamedXml).toContain('parent="newId"');
        expect(renamedXml).not.toContain('oldId');
    });

    it('renames cell IDs inside compressed mxfile diagram content correctly', async () => {
        const rawXml = '<mxGraphModel><root><mxCell id="oldId" source="oldId" target="oldId" parent="oldId"/></root></mxGraphModel>';
        const compressedBase64 = await DrawioCodec.compress(rawXml);
        const mxfileXml = `<mxfile host="embed.diagrams.net"><diagram id="123" name="Page-1">${compressedBase64}</diagram></mxfile>`;

        const renamedMxfile = await DrawioCodec.renameCellId(mxfileXml, 'oldId', 'newId');
        expect(renamedMxfile).toContain('<mxfile');
        expect(renamedMxfile).toContain('<diagram');

        // Extract the new compressed base64 string and decompress it to verify contents
        const match = renamedMxfile.match(/<diagram[^>]*>([^<]+)<\/diagram>/);
        expect(match).toBeDefined();
        const newCompressedBase64 = match![1];
        const decompressedRenamed = await DrawioCodec.decompress(newCompressedBase64);

        expect(decompressedRenamed).toContain('id="newId"');
        expect(decompressedRenamed).toContain('source="newId"');
        expect(decompressedRenamed).toContain('target="newId"');
        expect(decompressedRenamed).toContain('parent="newId"');
        expect(decompressedRenamed).not.toContain('oldId');
    });

    it('wraps raw mxCell in an object wrapper with name attribute so it shows up in draw.io', async () => {
        const rawXml = '<mxGraphModel><root><mxCell id="oldId" value="My Pump" style="ellipse" vertex="1" parent="1"/></root></mxGraphModel>';
        const renamedXml = await DrawioCodec.renameCellId(rawXml, 'oldId', 'pump_1');
        expect(renamedXml).toContain('<object id="pump_1" name="pump_1" label="My Pump">');
        expect(renamedXml).toContain('<mxCell style="ellipse" vertex="1" parent="1"/>');
        expect(renamedXml).toContain('</object>');
    });

    it('updates existing object wrapper with new id and name attribute correctly', async () => {
        const rawXml = '<mxGraphModel><root><object id="oldId" name="oldName" label="My Valve"><mxCell style="rect" vertex="1" parent="1"/></object></root></mxGraphModel>';
        const renamedXml = await DrawioCodec.renameCellId(rawXml, 'oldId', 'valve_1');
        expect(renamedXml).toContain('<object id="valve_1" name="valve_1" label="My Valve">');
        expect(renamedXml).toContain('<mxCell style="rect" vertex="1" parent="1"/>');
        expect(renamedXml).toContain('</object>');
    });
});
