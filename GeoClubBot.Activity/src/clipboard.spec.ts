import { afterEach, describe, expect, it, vi } from 'vitest';
import { copyText } from './clipboard';

const originalClipboard = Object.getOwnPropertyDescriptor(navigator, 'clipboard');

function setClipboard(value: unknown): void {
  Object.defineProperty(navigator, 'clipboard', { value, configurable: true });
}

// jsdom doesn't implement document.execCommand, so tests define it explicitly.
function setExecCommand(result: boolean): ReturnType<typeof vi.fn> {
  const exec = vi.fn().mockReturnValue(result);
  (document as unknown as { execCommand: unknown }).execCommand = exec;
  return exec;
}

afterEach(() => {
  if (originalClipboard) {
    Object.defineProperty(navigator, 'clipboard', originalClipboard);
  } else {
    setClipboard(undefined);
  }
  delete (document as unknown as { execCommand?: unknown }).execCommand;
  vi.restoreAllMocks();
});

describe('copyText', () => {
  it('uses the async clipboard API when it is available', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    setClipboard({ writeText });

    await expect(copyText('otp-1234')).resolves.toBe(true);
    expect(writeText).toHaveBeenCalledWith('otp-1234');
  });

  it('falls back to execCommand when the clipboard API is blocked (Discord iframe)', async () => {
    setClipboard({ writeText: vi.fn().mockRejectedValue(new Error('NotAllowedError')) });
    const exec = setExecCommand(true);

    await expect(copyText('otp-1234')).resolves.toBe(true);
    expect(exec).toHaveBeenCalledWith('copy');
  });

  it('reports failure when neither path can copy', async () => {
    setClipboard(undefined);
    setExecCommand(false);

    await expect(copyText('otp-1234')).resolves.toBe(false);
  });
});
