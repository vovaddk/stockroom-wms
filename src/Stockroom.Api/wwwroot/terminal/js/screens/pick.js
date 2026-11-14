import { api } from '../api.js';
import {
  h, button, details, scanForm, showForm, showStatus, showError, showNotice, rejectField, focusField, parseQuantity, confirmDialog,
} from '../ui.js';

export function render(root) {
  const view = h('div');
  root.append(view);
  load();

  async function load() {
    showNotice(view, 'Loading…');
    let task;
    try {
      task = await api('pick-tasks/next'); // null on 204: nothing to pick
    } catch (error) {
      showError(error);
      return showNotice(view, 'Could not load the next pick task.', 'Retry', load);
    }
    if (!task) return showNotice(view, 'No pick tasks. Ask a manager to allocate an order.', 'Refresh', load);
    showTask(task);
  }

  function showTask(task) {
    const step = h('div');
    view.replaceChildren(
      h('p', { class: 'task-meta' }, `Order ${task.orderNumber}, line ${task.sequence}`),
      h('p', { class: 'big-label' }, 'Go to'),
      h('p', { class: 'big-code' }, task.locationCode),
      details([
        ['Product', `${task.productName} (${task.sku})`],
        ['Lot', task.lotNumber],
        ['Expiry', task.expiryDate],
        ['Pick', task.quantity],
      ]),
      step);
    scanLocation();

    function scanLocation() {
      showForm(step, scanForm({
        label: 'Step 1 of 3: scan location',
        submitLabel: 'Next',
        onSubmit: (code, input) => (code.toUpperCase() === task.locationCode.toUpperCase()
          ? scanProduct(code.toUpperCase())
          : rejectField(input, `Wrong location, go to ${task.locationCode}`)),
      }));
    }

    function scanProduct(locationCode) {
      showForm(step, scanForm({
        label: 'Step 2 of 3: scan product barcode',
        submitLabel: 'Next',
        laterStep: true,
        onSubmit: (barcode, input) => (barcode === task.barcode
          ? enterQuantity(locationCode, barcode)
          : rejectField(input, `Wrong product, scan ${task.productName} (${task.sku})`)),
      }));
    }

    function enterQuantity(locationCode, barcode) {
      const field = scanForm({
        label: 'Step 3 of 3: quantity picked',
        hint: 'Found fewer? Use Short pick and enter what you picked.',
        inputmode: 'numeric',
        value: String(task.quantity),
        submitLabel: 'Confirm pick',
        laterStep: true,
        onSubmit: (text, input) => confirmPick(locationCode, barcode, text, input),
      });
      field.form.append(button('Short pick', () => {
        field.input.value = '';
        field.input.focus();
      }));
      showForm(step, field);
    }

    async function confirmPick(locationCode, barcode, text, input) {
      const quantity = parseQuantity(text);
      if (quantity === null || quantity > task.quantity) return rejectField(input, `Enter a whole number from 0 to ${task.quantity}`);
      if (quantity < task.quantity && !(await confirmDialog(`Confirm short pick: ${quantity} of ${task.quantity}?`, 'Confirm short pick'))) {
        return focusField(input);
      }

      let result;
      try {
        result = await api(`pick-tasks/${task.id}/confirm`, { method: 'POST', body: { locationCode, barcode, quantity } });
      } catch (error) {
        showError(error);
        return error.status === 409 ? load() : focusField(input);
      }

      const isShort = result.status === 'Short';
      const lines = [isShort
        ? `Short pick recorded: ${result.pickedQuantity} of ${task.quantity} ${task.sku}.`
        : `Picked ${result.pickedQuantity} of ${task.sku} for ${result.orderNumber}.`];
      if (result.orderStatus === 'Picked') lines.push(`Order ${result.orderNumber} fully picked.`);
      showStatus(lines, isShort ? 'warning' : 'success');
      return load();
    }
  }
}
