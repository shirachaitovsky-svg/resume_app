import { InjectionToken } from '@angular/core';

export const PHOTO_MAX_SIDE = 800;
export const PHOTO_TARGET_BYTES = 300 * 1024;
export const PHOTO_HARD_LIMIT_BYTES = 400 * 1024;
export const PHOTO_TYPES: readonly string[] = ['image/jpeg', 'image/png', 'image/webp'];

export const PHOTO_MAX_INPUT_BYTES = 25 * 1024 * 1024;
const QUALITIES = [0.85, 0.75, 0.65, 0.55, 0.45, 0.4];
const SCALE_STEPS = 4;

export class PhotoError extends Error {
  constructor(readonly kind: 'unsupported' | 'tooLarge') {
    super(kind);
  }
}

/** A decoded image that can be re-encoded at any size and quality. */
export interface DecodedImage {
  readonly width: number;
  readonly height: number;
  encode(width: number, height: number, quality: number): Promise<Blob | null>;
  close?(): void;
}

export type ImageDecoder = (file: Blob) => Promise<DecodedImage>;

/** Long side capped at `max`; never enlarges. Pure so it needs no DOM. */
export function fitDimensions(width: number, height: number, max: number): { width: number; height: number } {
  const longest = Math.max(width, height);
  const ratio = longest > max ? max / longest : 1;
  return { width: Math.max(1, Math.round(width * ratio)), height: Math.max(1, Math.round(height * ratio)) };
}

/** Browser decoder: `createImageBitmap` plus a canvas (white background, so transparent PNGs stay readable as JPEG). */
export const decodeImage: ImageDecoder = async (file) => {
  const bitmap = await createImageBitmap(file);
  return {
    width: bitmap.width,
    height: bitmap.height,
    encode: (width, height, quality) => {
      const canvas = document.createElement('canvas');
      canvas.width = width;
      canvas.height = height;
      const ctx = canvas.getContext('2d');
      if (!ctx) return Promise.resolve(null);
      ctx.fillStyle = '#ffffff';
      ctx.fillRect(0, 0, width, height);
      ctx.drawImage(bitmap, 0, 0, width, height);
      return new Promise<Blob | null>((resolve) => canvas.toBlob(resolve, 'image/jpeg', quality));
    },
    close: () => bitmap.close(),
  };
};

export const IMAGE_DECODER = new InjectionToken<ImageDecoder>('IMAGE_DECODER', {
  providedIn: 'root',
  factory: () => decodeImage,
});

/**
 * Shrinks a picked image to a JPEG of about 300KB (never above 400KB).
 * Throws `PhotoError('unsupported')` for non-images/undecodable files and `PhotoError('tooLarge')` when it cannot fit.
 */
export async function resizeImage(file: Blob, decode: ImageDecoder = decodeImage): Promise<Blob> {
  if (file.type !== '' && !PHOTO_TYPES.includes(file.type)) throw new PhotoError('unsupported');
  if (file.size > PHOTO_MAX_INPUT_BYTES) throw new PhotoError('tooLarge');
  let image: DecodedImage;
  try {
    image = await decode(file);
  } catch {
    throw new PhotoError('unsupported');
  }
  try {
    if (!(image.width > 0) || !(image.height > 0)) throw new PhotoError('unsupported');
    let best: Blob | null = null;
    let side = PHOTO_MAX_SIDE;
    let prev = '';
    for (let step = 0; step < SCALE_STEPS; step++) {
      const { width, height } = fitDimensions(image.width, image.height, side);
      const key = `${width}x${height}`;
      if (key === prev) {
        side = Math.max(1, Math.floor(side * 0.8));
        continue;
      }
      prev = key;
      for (const quality of QUALITIES) {
        const blob = await image.encode(width, height, quality);
        if (!blob) continue;
        if (blob.size <= PHOTO_TARGET_BYTES) return blob;
        if (!best || blob.size < best.size) best = blob;
      }
      side = Math.max(1, Math.floor(side * 0.8));
    }
    if (best && best.size <= PHOTO_HARD_LIMIT_BYTES) return best;
    throw new PhotoError('tooLarge');
  } finally {
    image.close?.();
  }
}
