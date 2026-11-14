import { api } from '../api.js';
import { h, details, scanForm, showForm, showStatus, rejectField, focusField } from '../ui.js';

// Bin codes look like A-01-03-2 (storage) or RCV-01 (dock); anything else is treated as a product barcode.
const BIN_PATTERN = /^[A-Z]{1,4}-\d{2}(-\d{2}-\d)?$/i;

export function render(root) {
  const scan = h('div');
  const results = h('ul', { class: 'cards', 'aria-label': 'Stock results' });
  root.append(scan, results);

  showForm(scan, scanForm({
    label: 'Scan barcode or bin',
    submitLabel: 'Look up',
    onSubmit: async (code, input) => {
      const query = BIN_PATTERN.test(code)
        ? `locationCode=${encodeURIComponent(code.toUpperCase())}`
        : `barcode=${encodeURIComponent(code)}`;
      let page;
      try {
        page = await api(`stock?${query}&page=1&pageSize=50`);
      } catch (error) {
        results.replaceChildren();
        return rejectField(input, error);
      }

      const more = page.totalCount > page.items.length ? ` Showing the first ${page.items.length}.` : '';
      showStatus(page.totalCount === 0 ? 'No stock found' : `${page.totalCount} stock ${page.totalCount === 1 ? 'record' : 'records'} for ${code}.${more}`, 'info');
      results.replaceChildren(...page.items.map(stockCard));
      return focusField(input); // ready for the next scan
    },
  }));
}

function stockCard(item) {
  return h('li', { class: 'card' },
    h('p', { class: 'card-title' }, h('span', { class: 'code' }, item.locationCode), ` Zone ${item.zoneCode}`),
    h('p', {}, `${item.productName} (${item.sku})`),
    details([
      ['Lot', item.lotNumber],
      ['Expiry', item.expiryDate],
      ['On hand', item.quantity],
      ['Reserved', item.reservedQuantity],
      ['Available', item.availableQuantity],
    ]));
}
