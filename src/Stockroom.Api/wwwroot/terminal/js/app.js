import { session } from './api.js';
import { h, button, clearMessages, showError } from './ui.js';
import * as login from './screens/login.js';
import * as receive from './screens/receive.js';
import * as putaway from './screens/putaway.js';
import * as pick from './screens/pick.js';
import * as stock from './screens/stock.js';

const routes = {
  receive: { title: 'Receive', render: receive.render },
  putaway: { title: 'Put-away', render: putaway.render },
  pick: { title: 'Pick', render: pick.render },
  stock: { title: 'Stock lookup', render: stock.render },
};

const main = document.getElementById('screen');
const goHome = () => { location.hash = '#/'; };

function renderHome(root) {
  const menu = h('nav', { class: 'menu', 'aria-label': 'Tasks' },
    Object.entries(routes).map(([name, route]) => button(route.title, () => { location.hash = `#/${name}`; }, 'btn btn-menu')));
  root.append(menu);
  menu.querySelector('button').focus();
}

function render() {
  clearMessages();
  const user = session.get();
  document.getElementById('user-info').hidden = !user;
  if (user) {
    document.getElementById('user-name').textContent = user.userName;
    document.getElementById('user-role').textContent = user.role;
  }

  // #/receive/42 -> name "receive", param "42"
  const [name, param] = location.hash.replace(/^#\/?/, '').split('/');
  const isTaskScreen = user && Object.hasOwn(routes, name);
  const route = !user ? { title: 'Sign in', render: root => login.render(root, render) }
    : isTaskScreen ? routes[name]
    : { title: 'Home', render: renderHome };

  document.title = `${route.title} - Stockroom Terminal`;
  const root = h('section', { class: 'screen', 'aria-labelledby': 'screen-title' },
    h('div', { class: 'screen-head' },
      isTaskScreen && button('Back', goHome, 'btn btn-back'),
      h('h2', { id: 'screen-title' }, route.title)));
  main.replaceChildren(root);
  main.focus(); // screens move focus to their first field once rendered
  route.render(root, param && decodeURIComponent(param));
}

document.getElementById('sign-out').addEventListener('click', () => {
  session.clear();
  history.replaceState(null, '', '#/');
  render();
});

window.addEventListener('session-expired', () => {
  render();
  showError('Session expired, please sign in again');
});

// Escape leaves a task screen from its first step; later steps and dialogs opt out via data-later-step.
document.addEventListener('keydown', event => {
  if (event.key === 'Escape' && session.get() && !document.activeElement.closest('[data-later-step]')) goHome();
});

window.addEventListener('hashchange', render);
render();
