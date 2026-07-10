import { describe, expect, it } from 'vitest';
import { parseGeoGuessrUserId } from './parse';

describe('parseGeoGuessrUserId', () => {
  const id = '62c353a29d0d57e7b9a3383f';

  it('extracts the id from a profile share link', () => {
    expect(parseGeoGuessrUserId(`https://www.geoguessr.com/user/${id}`)).toBe(id);
  });

  it('accepts a raw id and lowercases it', () => {
    expect(parseGeoGuessrUserId(id.toUpperCase())).toBe(id);
  });

  it('ignores surrounding whitespace', () => {
    expect(parseGeoGuessrUserId(`  https://www.geoguessr.com/user/${id}  `)).toBe(id);
  });

  it('returns null for input without a 24-hex id', () => {
    expect(parseGeoGuessrUserId('https://example.com/not-a-profile')).toBeNull();
    expect(parseGeoGuessrUserId('')).toBeNull();
  });
});
