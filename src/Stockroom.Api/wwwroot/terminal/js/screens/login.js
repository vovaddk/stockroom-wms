import { api, session } from '../api.js';
import { h, handleSubmit, rejectField } from '../ui.js';

export function render(root, onSignedIn) {
  const userName = h('input', { id: 'login-user', type: 'text', autocomplete: 'username', autocapitalize: 'none', spellcheck: 'false', required: true });
  const password = h('input', { id: 'login-password', type: 'password', autocomplete: 'current-password', required: true });
  const form = h('form', { class: 'scan' },
    h('label', { for: 'login-user' }, 'User name'), userName,
    h('label', { for: 'login-password' }, 'Password'), password,
    h('button', { type: 'submit', class: 'btn btn-primary' }, 'Sign in'));

  handleSubmit(form, async () => {
    try {
      const body = { userName: userName.value.trim(), password: password.value };
      session.save(await api('auth/login', { method: 'POST', body, anonymous: true }));
    } catch (error) {
      return rejectField(password, error);
    }
    onSignedIn();
  });

  root.append(form);
  userName.focus();
}
