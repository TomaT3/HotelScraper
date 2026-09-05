/**
 * Extracts the backend `detail` field from an API error message
 * ("API error 400: {\"detail\":\"...\"}") for inline display.
 */
export function apiErrorDetail(err: unknown): string {
  if (err instanceof Error) {
    const m = err.message.match(/API error \d+: (.+)/);
    if (m) {
      try {
        const parsed = JSON.parse(m[1]);
        if (parsed && typeof parsed.detail === "string") return parsed.detail;
      } catch {
        // fall through to raw message
      }
      return m[1];
    }
    return err.message;
  }
  return String(err);
}
