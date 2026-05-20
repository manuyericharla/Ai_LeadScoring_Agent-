export function resolveApiBase(): string {
  if (typeof window === 'undefined') {
    return 'http://localhost:5221';
  }

  const { hostname, origin } = window.location;
  if (hostname === 'localhost' || hostname === '127.0.0.1') {
    return 'http://localhost:5221';
  }

  return origin;
}

export function apiUrl(path: string): string {
  const base = resolveApiBase().replace(/\/$/, '');
  const normalized = path.startsWith('/') ? path : `/${path}`;
  return `${base}${normalized}`;
}
