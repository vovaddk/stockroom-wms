const SESSION_KEY = 'stockroom.session';

export const session = {
  get() {
    const saved = JSON.parse(sessionStorage.getItem(SESSION_KEY) ?? 'null');
    return saved && Date.parse(saved.expiresAt) > Date.now() ? saved : null;
  },
  save(login) {
    const { accessToken, expiresAt, userName, role } = login;
    sessionStorage.setItem(SESSION_KEY, JSON.stringify({ accessToken, expiresAt, userName, role }));
  },
  clear() {
    sessionStorage.removeItem(SESSION_KEY);
  },
};

/** Carries an RFC 7807 ProblemDetails body (or a synthetic one) plus the HTTP status. */
export class ApiError extends Error {
  constructor(status, problem) {
    super(problem.title);
    this.status = status;
    this.problem = problem;
  }
}

/** Calls /api/{path}. Resolves with the JSON body, or null for 204 No Content. */
export async function api(path, { method = 'GET', body, anonymous = false } = {}) {
  const user = anonymous ? null : session.get();
  const headers = { Accept: 'application/json' };
  if (body !== undefined) headers['Content-Type'] = 'application/json';
  if (user) headers.Authorization = `Bearer ${user.accessToken}`;

  let response;
  try {
    response = await fetch(`/api/${path}`, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) });
  } catch {
    throw new ApiError(0, { title: 'Cannot reach server' });
  }

  if (response.status === 401 && !anonymous) {
    session.clear();
    window.dispatchEvent(new Event('session-expired'));
    const error = new ApiError(401, { title: 'Session expired' });
    error.handled = true; // the app already shows the sign-in screen with its own message
    throw error;
  }
  if (response.status === 204) return null;

  const data = await response.json().catch(() => null);
  if (!response.ok) throw new ApiError(response.status, data?.title ? data : { title: `Request failed (${response.status})` });
  return data;
}
