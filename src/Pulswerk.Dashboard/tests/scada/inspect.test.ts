import { describe, it } from 'vitest';
import { DrawioCodec } from '../../src/frontend/dashboards/scada/drawio.codec';

describe('Inspect New Example', () => {
    it('decompresses new example base64', async () => {
        const b64 = '1VSxUoQwEP2a9ECcE9vD866xorDOkT0SDYQJUcCvN1w2EOZ0xsLGit2Xt/uybzMQWjTj0bBOPGsOimQJHwl9JFmW0py6z4xMHsnz3AO1kRxJK1DKT0AwQfRdcug3RKu1srLbgpVuW6jsBmPG6GFLu2i1Ve1YDTdAWTF1i75IbgVOkd2v+AlkLYJyunvwJw0LZJykF4zrIYLogdDCaG191IwFqNm84Iuve/rhdLmYgdZ+U6DPr7MfWaLY2e3kSvAloYOS7ZuzNtxkUYhbokpvp2DIIKSFsmPVnA9u6YTuhW1mhdSFrO/8Gi5yBCe09w0+wFgYo54oeQTdgDWTo4jIxhw9GyLLEcImd5jis6KYMlx3vfRdHXEBzhhS9OiXfhmohP23hqW7v3bMpevrvVKjfwA9fAE=';
        const decomp = await DrawioCodec.decompress(b64);
        console.log('NEW EXAMPLE DECOMPRESSED:', decomp);
    });
});
