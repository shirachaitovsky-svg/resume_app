import { DecodedImage, fitDimensions, resizeImage } from './photo-resize';

const KB = 1024;

function image(width: number, height: number, sizeFor: (w: number, h: number, q: number) => number): DecodedImage & { calls: number[][] } {
  const calls: number[][] = [];
  return {
    width,
    height,
    calls,
    encode: async (w, h, q) => {
      calls.push([w, h, q]);
      return new Blob([new Uint8Array(sizeFor(w, h, q))], { type: 'image/jpeg' });
    },
  };
}
const file = (type: string) => new Blob(['x'], { type });

describe('fitDimensions', () => {
  it('caps the long side and keeps the ratio', () => {
    expect(fitDimensions(4000, 2000, 800)).toEqual({ width: 800, height: 400 });
    expect(fitDimensions(1000, 3000, 800)).toEqual({ width: 267, height: 800 });
  });
  it('never enlarges and never returns 0', () => {
    expect(fitDimensions(300, 200, 800)).toEqual({ width: 300, height: 200 });
    expect(fitDimensions(10000, 1, 800)).toEqual({ width: 800, height: 1 });
  });
});

describe('resizeImage', () => {
  it('lets the decoder try when the file type is empty', async () => {
    const img = image(300, 200, () => 20 * KB);
    expect((await resizeImage(file(''), async () => img)).size).toBe(20 * KB);
    await expect(resizeImage(file(''), () => Promise.reject(new Error('x')))).rejects.toMatchObject({ kind: 'unsupported' });
  });

  it('rejects inputs over 25MB before decoding', async () => {
    const big = { type: 'image/jpeg', size: 26 * 1024 * 1024 } as Blob;
    const decode = vi.fn();
    await expect(resizeImage(big, decode)).rejects.toMatchObject({ kind: 'tooLarge' });
    expect(decode).not.toHaveBeenCalled();
  });

  it('does not repeat identical encodes for small images', async () => {
    const img = image(300, 200, () => 900 * KB);
    await expect(resizeImage(file('image/jpeg'), async () => img)).rejects.toMatchObject({ kind: 'tooLarge' });
    expect(img.calls.length).toBe(6);
  });

  it('rejects unsupported types without decoding', async () => {
    const decode = vi.fn(() => Promise.reject(new Error('should not decode')));
    await expect(resizeImage(file('text/plain'), decode)).rejects.toMatchObject({ kind: 'unsupported' });
    expect(decode).not.toHaveBeenCalled();
  });

  it('maps a decode failure to unsupported', async () => {
    await expect(resizeImage(file('image/png'), () => Promise.reject(new Error('bad')))).rejects.toMatchObject({ kind: 'unsupported' });
  });

  it('keeps a small image at full size and top quality', async () => {
    const img = image(300, 200, () => 20 * KB);
    const blob = await resizeImage(file('image/png'), async () => img);
    expect(blob.size).toBe(20 * KB);
    expect(img.calls).toEqual([[300, 200, 0.85]]);
  });

  it('lowers quality until about 300KB', async () => {
    const img = image(2000, 1000, (_w, _h, q) => Math.round(q * 400 * KB));
    const blob = await resizeImage(file('image/jpeg'), async () => img);
    expect(blob.size).toBeLessThanOrEqual(300 * KB);
    expect(img.calls[0].slice(0, 2)).toEqual([800, 400]);
  });

  it('shrinks the side when quality alone is not enough', async () => {
    const img = image(2000, 2000, (w) => (w > 700 ? 500 * KB : 100 * KB));
    const blob = await resizeImage(file('image/webp'), async () => img);
    expect(blob.size).toBe(100 * KB);
    expect(img.calls.some(([w]) => w < 800)).toBe(true);
  });

  it('accepts a result between 300KB and 400KB as a last resort', async () => {
    const img = image(2000, 2000, () => 350 * KB);
    expect((await resizeImage(file('image/jpeg'), async () => img)).size).toBe(350 * KB);
  });

  it('fails when it cannot get under 400KB', async () => {
    const img = image(2000, 2000, () => 900 * KB);
    await expect(resizeImage(file('image/jpeg'), async () => img)).rejects.toMatchObject({ kind: 'tooLarge' });
  });
});

