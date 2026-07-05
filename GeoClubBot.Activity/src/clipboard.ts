/**
 * Copies text to the clipboard, returning whether it succeeded.
 *
 * The async Clipboard API (`navigator.clipboard`) is the preferred path, but Discord's activity
 * iframe is cross-origin and isn't granted the `clipboard-write` permission, so `writeText` rejects
 * there (and `navigator.clipboard` can be undefined outside secure contexts). We fall back to a
 * legacy `execCommand('copy')` on a hidden textarea, which still works inside a user-gesture handler
 * in that sandbox.
 */
export async function copyText(text: string): Promise<boolean> {
  try {
    if (navigator.clipboard?.writeText) {
      await navigator.clipboard.writeText(text);
      return true;
    }
  } catch {
    // Blocked (e.g. no clipboard-write permission in the iframe) — try the legacy path.
  }

  return legacyCopy(text);
}

function legacyCopy(text: string): boolean {
  try {
    const textarea = document.createElement('textarea');
    textarea.value = text;
    textarea.setAttribute('readonly', '');
    textarea.style.position = 'fixed';
    textarea.style.top = '-9999px';
    document.body.appendChild(textarea);

    const selection = document.getSelection();
    const savedRange = selection && selection.rangeCount > 0 ? selection.getRangeAt(0) : null;

    textarea.select();
    const succeeded = document.execCommand('copy');

    document.body.removeChild(textarea);
    if (savedRange && selection) {
      selection.removeAllRanges();
      selection.addRange(savedRange);
    }
    return succeeded;
  } catch {
    return false;
  }
}
