export function normalizeUrlPathSegment(value: string): string {
  return value.trim().replace(/^\/+|\/+$/g, '');
}

export function toSafeHttpsOrigin(origin: string): string {
  const trimmed = (origin ?? '').trim().replace(/\/+$/, '');
  if (!trimmed) {
    return '';
  }

  try {
    const url = new URL(trimmed);
    const isLocalHost =
      url.hostname === 'localhost' ||
      url.hostname === '127.0.0.1' ||
      url.hostname === '[::1]' ||
      url.hostname === '::1';

    if (url.protocol === 'http:' && !isLocalHost) {
      url.protocol = 'https:';
    }
    return `${url.origin}${url.pathname.replace(/\/+$/, '')}`;
  } catch {
    return trimmed;
  }
}
