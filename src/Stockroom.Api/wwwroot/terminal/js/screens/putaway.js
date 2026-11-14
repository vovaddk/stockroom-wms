import { api } from '../api.js';
import { h, button, details, scanForm, showForm, showStatus, showError, showNotice, focusField } from '../ui.js';

export function render(root) {
  const view = h('div');
  root.append(view);
  refresh();

  /** Reloads open tasks; `select` picks the task to open, otherwise the list is shown. */
  async function refresh(select = () => undefined) {
    showNotice(view, 'Loading…');
    let tasks;
    try {
      // shortcut: only the first 50 open tasks are loaded; add paging when queues grow beyond that.
      tasks = (await api('putaway-tasks?status=Open&page=1&pageSize=50')).items;
    } catch (error) {
      showError(error);
      return showNotice(view, 'Could not load put-away tasks.', 'Retry', () => refresh(select));
    }
    if (tasks.length === 0) return showNotice(view, 'No open put-away tasks.', 'Refresh', () => refresh());

    const task = select(tasks);
    return task ? showTask(task) : showList(tasks);
  }

  function showList(tasks) {
    view.replaceChildren(h('ul', { class: 'choices' }, tasks.map(task => h('li', {},
      button([
        h('span', { class: 'choice-title' }, task.productName),
        h('span', {}, `${task.quantity} of ${task.sku}${task.lotNumber ? `, lot ${task.lotNumber}` : ''}`),
        h('span', { class: 'choice-meta' }, `From ${task.fromLocationCode} to ${task.suggestedLocationCode ?? 'any bin'}`),
      ], () => showTask(task), 'btn choice')))));
    view.querySelector('button').focus();
  }

  function showTask(task) {
    const step = h('div');
    view.replaceChildren(
      h('p', { class: 'big-label' }, 'Put away to'),
      task.suggestedLocationCode
        ? h('p', { class: 'big-code' }, task.suggestedLocationCode)
        : h('p', { class: 'big-note' }, 'No suggestion - scan any storage bin'),
      details([
        ['Product', `${task.productName} (${task.sku})`],
        ['Lot', task.lotNumber],
        ['Quantity', task.quantity],
        ['From', task.fromLocationCode],
      ]),
      step,
      button('Choose another task', () => refresh()));

    showForm(step, scanForm({
      label: 'Scan destination bin',
      submitLabel: 'Confirm put-away',
      onSubmit: async (code, input) => {
        const locationCode = code.toUpperCase();
        try {
          await api(`putaway-tasks/${task.id}/confirm`, { method: 'POST', body: { locationCode } });
        } catch (error) {
          showError(error);
          return error.status === 409 ? refresh(tasks => tasks.find(t => t.id === task.id)) : focusField(input);
        }
        showStatus(`Put away ${task.quantity} of ${task.sku} to ${locationCode}.`);
        return refresh(tasks => tasks[0]);
      },
    }));
  }
}
