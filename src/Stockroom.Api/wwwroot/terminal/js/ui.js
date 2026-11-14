const statusRegion = document.getElementById('status');
const alertRegion = document.getElementById('alert');

/** Builds an element. Children are appended as nodes or text, never parsed as HTML. */
export function h(tag, props = {}, ...children) {
  const element = document.createElement(tag);
  for (const [key, value] of Object.entries(props)) {
    if (value == null || value === false) continue;
    if (key === 'class') element.className = value;
    else if (key.startsWith('on')) element.addEventListener(key.slice(2), value);
    else element.setAttribute(key, value === true ? '' : value);
  }
  element.append(...children.flat().filter(child => child != null && child !== false));
  return element;
}

export const button = (label, onclick, className = 'btn') => h('button', { type: 'button', class: className, onclick }, label);

export function details(rows) {
  return h('dl', { class: 'details' }, rows.flatMap(([term, value]) => [h('dt', {}, term), h('dd', {}, value ?? 'None')]));
}

export function clearMessages() {
  statusRegion.replaceChildren();
  alertRegion.replaceChildren();
}

/** kind: success | warning | info */
export function showStatus(lines, kind = 'success') {
  alertRegion.replaceChildren();
  statusRegion.className = `msg msg-${kind}`;
  statusRegion.replaceChildren(...[lines].flat().map(line => h('p', {}, line)));
}

/** Accepts a message string or an error (ApiError carries ProblemDetails). */
export function showError(error) {
  if (error.handled) return;
  const problem = typeof error === 'string' ? { title: error } : error.problem ?? { title: error.message };
  const messages = Object.values(problem.errors ?? {}).flat();
  statusRegion.replaceChildren();
  alertRegion.replaceChildren(...[
    h('p', { class: 'msg-title' }, problem.title),
    problem.detail && h('p', {}, problem.detail),
    messages.length > 0 && h('ul', {}, messages.map(message => h('li', {}, message))),
    error.status === 409 && h('p', {}, 'Someone else changed this item. Reload and try again.'),
  ].filter(Boolean));
}

export function focusField(input) {
  input.focus();
  input.select();
}

export function rejectField(input, error) {
  showError(error);
  focusField(input);
}

export const parseQuantity = text => (/^\d+$/.test(text) ? Number(text) : null);

/** Message plus an optional action button (Retry, Refresh), which gets focus. */
export function showNotice(container, message, actionLabel, action) {
  const actionButton = actionLabel && button(actionLabel, action);
  container.replaceChildren(...[h('p', { class: 'notice' }, message), actionButton].filter(Boolean));
  actionButton?.focus();
}

/** Prevents page reloads and double submits (scanners can send Enter twice). */
export function handleSubmit(form, handler) {
  form.addEventListener('submit', async event => {
    event.preventDefault();
    if (form.getAttribute('aria-busy') === 'true') return;
    form.setAttribute('aria-busy', 'true');
    clearMessages();
    try {
      await handler();
    } finally {
      form.setAttribute('aria-busy', 'false');
    }
  });
}

let fieldCount = 0;

/**
 * One labelled scan field; Enter submits. onSubmit(value, input) runs only for non-empty values.
 * laterStep marks fields past a screen's first step, where Escape must not leave the screen.
 */
export function scanForm({ label, onSubmit, submitLabel = 'OK', inputmode, value = '', hint, laterStep = false }) {
  const id = `scan-${++fieldCount}`;
  const input = h('input', {
    id, type: 'text', inputmode, value, autocomplete: 'off', autocapitalize: 'none', spellcheck: 'false',
    enterkeyhint: 'go', 'aria-describedby': hint && `${id}-hint`,
  });
  const form = h('form', { class: 'scan', 'data-later-step': laterStep },
    h('label', { for: id }, label),
    hint && h('p', { id: `${id}-hint`, class: 'hint' }, hint),
    input,
    h('button', { type: 'submit', class: 'btn btn-primary' }, submitLabel));
  handleSubmit(form, () => {
    const text = input.value.trim();
    return text ? onSubmit(text, input) : focusField(input);
  });
  return { form, input };
}

export function showForm(container, { form, input }) {
  container.replaceChildren(form);
  focusField(input);
}

/** Modal confirm; resolves true only when the confirm button is chosen (Escape cancels). */
export function confirmDialog(message, confirmLabel = 'Confirm') {
  const dialog = h('dialog', { class: 'dialog', 'aria-labelledby': 'dialog-message', 'data-later-step': true },
    h('form', { method: 'dialog' },
      h('p', { id: 'dialog-message' }, message),
      h('div', { class: 'actions' },
        h('button', { class: 'btn', value: 'cancel', autofocus: true }, 'Cancel'),
        h('button', { class: 'btn btn-warning', value: 'confirm' }, confirmLabel))));
  document.body.append(dialog);
  dialog.showModal();
  return new Promise(resolve => dialog.addEventListener('close', () => {
    dialog.remove();
    resolve(dialog.returnValue === 'confirm');
  }));
}
