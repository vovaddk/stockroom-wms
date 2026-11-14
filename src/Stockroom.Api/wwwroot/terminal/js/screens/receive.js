import { api } from '../api.js';
import {
  h, button, scanForm, showForm, handleSubmit, showStatus, showError, showNotice, rejectField, focusField, parseQuantity,
} from '../ui.js';

// shortcut: only the first 50 open ASNs are loaded; add paging when a site has more.
const loadOpenAsns = async () => (await api('asns?status=Open&page=1&pageSize=50')).items;
const goToPutAway = () => { location.hash = '#/putaway'; };
const sum = (lines, key) => lines.reduce((total, line) => total + line[key], 0);

export function render(root, asnId) {
  return asnId ? renderAsn(root, asnId) : renderList(root);
}

function renderList(root) {
  const list = h('div');
  root.append(list, button('Go to put-away', goToPutAway));
  load();

  async function load() {
    showNotice(list, 'Loading…');
    let asns;
    try {
      asns = await loadOpenAsns();
    } catch (error) {
      showError(error);
      return showNotice(list, 'Could not load ASNs.', 'Retry', load);
    }
    if (asns.length === 0) return showNotice(list, 'No open ASNs.', 'Refresh', load);

    list.replaceChildren(h('ul', { class: 'choices' }, asns.map(asn => h('li', {},
      button([
        h('span', { class: 'choice-title' }, asn.number),
        h('span', {}, asn.supplier),
        h('span', { class: 'choice-meta' }, `${sum(asn.lines, 'receivedQuantity')} / ${sum(asn.lines, 'expectedQuantity')} received`),
      ], () => { location.hash = `#/receive/${encodeURIComponent(asn.id)}`; }, 'btn choice')))));
    list.querySelector('button').focus();
  }
}

function renderAsn(root, asnId) {
  const summary = h('div');
  const step = h('div');
  root.append(summary, step, button('Go to put-away', goToPutAway));
  let asn;
  reload();

  async function reload() {
    step.replaceChildren();
    showNotice(summary, 'Loading…');
    try {
      asn = (await loadOpenAsns()).find(candidate => String(candidate.id) === asnId);
    } catch (error) {
      showError(error);
      return showNotice(summary, 'Could not load the ASN.', 'Retry', reload);
    }
    if (!asn) return showNotice(summary, 'This ASN is no longer open.', 'Back to ASN list', () => { location.hash = '#/receive'; });

    summary.replaceChildren(h('p', { class: 'subtitle' }, `${asn.number} · ${asn.supplier}`), linesTable(asn.lines));
    scanBarcode();
  }

  function scanBarcode(value = '') {
    showForm(step, scanForm({
      label: 'Scan product barcode',
      submitLabel: 'Next',
      value,
      onSubmit: (barcode, input) => {
        const line = asn.lines.find(candidate => candidate.barcode === barcode);
        return line ? enterQuantity(line) : rejectField(input, `Barcode ${barcode} is not on ASN ${asn.number}`);
      },
    }));
  }

  function enterQuantity(line, value = '') {
    const remaining = Math.max(line.expectedQuantity - line.receivedQuantity, 0);
    showForm(step, scanForm({
      label: `Quantity of ${line.productName} (${line.sku})`,
      hint: `Expected ${remaining} more`,
      inputmode: 'numeric',
      submitLabel: 'Next',
      value,
      laterStep: true,
      onSubmit: (text, input) => {
        const quantity = parseQuantity(text);
        if (!quantity) return rejectField(input, 'Enter a whole number greater than 0');
        // Fail fast on the scanner; the server enforces the same rule.
        if (quantity > remaining) return rejectField(input, `Over-receipt: only ${remaining} more expected`);
        return enterLot(line, quantity);
      },
    }));
  }

  function enterLot(line, quantity) {
    const lot = h('input', {
      id: 'receive-lot', type: 'text', autocomplete: 'off', autocapitalize: 'none', spellcheck: 'false', 'aria-describedby': 'receive-lot-hint',
    });
    const expiry = h('input', { id: 'receive-expiry', type: 'date' });
    const form = h('form', { class: 'scan', 'data-later-step': true },
      h('p', { class: 'current' }, `${quantity} of ${line.productName} (${line.sku})`),
      h('label', { for: 'receive-lot' }, 'Lot number (optional)'),
      h('p', { id: 'receive-lot-hint', class: 'hint' }, 'Press Enter on an empty lot to skip.'),
      lot,
      h('label', { for: 'receive-expiry' }, 'Expiry date (optional)'),
      expiry,
      h('button', { type: 'submit', class: 'btn btn-primary' }, 'Receive'));

    // A scanned lot ends with Enter: move on to expiry instead of submitting.
    lot.addEventListener('keydown', event => {
      if (event.key === 'Enter' && lot.value.trim()) {
        event.preventDefault();
        expiry.focus();
      }
    });
    handleSubmit(form, () => submitReceipt(line, quantity, lot, expiry));
    showForm(step, { form, input: lot });
  }

  async function submitReceipt(line, quantity, lot, expiry) {
    let receipt;
    try {
      receipt = await api(`asns/${encodeURIComponent(asnId)}/receipts`, {
        method: 'POST',
        body: { barcode: line.barcode, quantity, lotNumber: lot.value.trim() || null, expiryDate: expiry.value || null },
      });
    } catch (error) {
      showError(error);
      // Return the operator to the field the server rejected.
      const fields = Object.keys(error.problem?.errors ?? {}).join(' ').toLowerCase();
      if (error.status === 409) return reload();
      if (fields.includes('barcode')) return scanBarcode(line.barcode);
      if (fields.includes('quantity')) return enterQuantity(line, String(quantity));
      return focusField(fields.includes('expiry') ? expiry : lot);
    }

    showStatus([
      `Received ${receipt.receivedQuantity} of ${receipt.sku}${receipt.lotNumber ? `, lot ${receipt.lotNumber}` : ''}.`,
      `Remaining on this line: ${receipt.remainingQuantity}.`,
      `Put away from ${receipt.receivingLocationCode} to ${receipt.suggestedLocationCode ?? 'any storage bin (no suggestion)'}.`,
    ]);
    reload();
  }
}

function linesTable(lines) {
  return h('table', { class: 'lines', 'aria-label': 'ASN lines' },
    h('thead', {}, h('tr', {},
      h('th', { scope: 'col' }, 'Product'),
      h('th', { scope: 'col', class: 'num' }, 'Received / expected'))),
    h('tbody', {}, lines.map(line => h('tr', { class: line.receivedQuantity >= line.expectedQuantity && 'done' },
      h('td', {}, line.productName, h('br'), h('small', {}, `${line.sku} · ${line.barcode}`)),
      h('td', { class: 'num' }, `${line.receivedQuantity} / ${line.expectedQuantity}`)))));
}
