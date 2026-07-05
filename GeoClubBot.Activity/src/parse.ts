/**
 * Extracts a GeoGuessr user id from user input: either the raw 24-hex id or a profile share link
 * ending in `/user/<id>`. Returns the lowercased id, or null when nothing matches.
 */
export function parseGeoGuessrUserId(input: string): string | null {
  const match = input.trim().match(/([a-f0-9]{24})\s*$/i);
  return match ? match[1].toLowerCase() : null;
}
