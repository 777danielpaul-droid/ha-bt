'use strict';

const $ = (id) => document.getElementById(id);
const eur = new Intl.NumberFormat('de-DE', { style: 'currency', currency: 'EUR' });
const pct = new Intl.NumberFormat('de-DE', { maximumFractionDigits: 1 });
const fmt = (n) => eur.format(n);
const reduceMotion = matchMedia('(prefers-reduced-motion: reduce)').matches;

let state = null;
let listTimer = null;

/* ---------- API (alles läuft über die C#-Services) ---------- */
async function api(path, options = {}) {
  let res;
  try {
    res = await fetch(path, {
      headers: { 'Content-Type': 'application/json' },
      ...options,
      body: options.body ? JSON.stringify(options.body) : undefined,
    });
    $('conn').classList.remove('off');
  } catch {
    $('conn').classList.add('off');
    throw new Error('Keine Verbindung zum Server.');
  }
  const data = await res.json().catch(() => ({}));
  if (!res.ok) throw new Error(data.error || `Fehler ${res.status}`);
  return data;
}

/* ---------- Helfer ---------- */
function parseAmount(text) {
  let s = String(text).trim().replace(/\s|€/g, '');
  if (!s) return NaN;
  if (s.includes(',')) s = s.replace(/\./g, '').replace(',', '.');
  else if (/^\d{1,3}(\.\d{3})+$/.test(s)) s = s.replace(/\./g, '');
  return /^\d+(\.\d+)?$/.test(s) ? Number(s) : NaN;
}

function toast(message, kind = '') {
  const el = document.createElement('div');
  el.className = 'toast ' + kind;
  el.textContent = message;
  $('toasts').appendChild(el);
  setTimeout(() => el.remove(), 3800);
}

const fail = (e) => toast(e.message, 'error');

/* ---------- Rendering ---------- */
function render(s) {
  state = s;
  $('setup').hidden = !s.needsSetup;
  $('app').hidden = s.needsSetup;
  $('bonusAmount').textContent = '+' + new Intl.NumberFormat('de-DE').format(s.bonusAmount) + ' €';
  if (s.needsSetup) { $('alert').hidden = true; return; }

  const b = s.budget;
  $('remaining').textContent = fmt(b.remaining);
  $('fTotal').textContent = fmt(b.total);
  $('fIncome').textContent = fmt(b.income);
  $('fSpent').textContent = fmt(b.spent);
  $('fPercent').textContent = pct.format(Math.max(b.remainingPercent, 0)) + ' %';

  const width = Math.min(Math.max(b.remainingPercent, 0), 100);
  $('fill').style.width = width + '%';
  $('mark').style.left = `calc(${b.warningThresholdPercent}% - 1px)`;
  $('bar').className = 'bar ' + (b.isExhausted ? 'out' : b.isLow ? 'low' : '');
  $('bar').setAttribute('aria-valuenow', Math.round(width));

  const chip = $('budgetChip');
  chip.className = 'chip ' + (b.isExhausted ? 'out' : b.isLow ? 'low' : 'ok');
  chip.textContent = b.isExhausted ? 'Aufgebraucht' : b.isLow ? 'Wird knapp' : 'Im grünen Bereich';

  const alert = $('alert');
  alert.hidden = !(b.isExhausted || b.isLow);
  alert.className = 'alert' + (b.isExhausted ? ' out' : '');
  alert.textContent = b.isExhausted
    ? 'Dein Budget ist aufgebraucht. Lade es unter „Budget aufladen“ wieder auf.'
    : `Dein Budget wird knapp: nur noch ${pct.format(b.remainingPercent)} % (${fmt(b.remaining)}) übrig.`;

  $('sIncome').textContent = fmt(s.totals.income);
  $('sExpense').textContent = fmt(s.totals.expenses);
  const bal = $('sBalance');
  bal.textContent = fmt(s.totals.balance);
  bal.className = 'stat-value ' + (s.totals.balance < 0 ? 'neg' : 'pos');

  if (document.activeElement !== $('thr')) $('thr').value = String(b.warningThresholdPercent).replace('.', ',');
}

function buildRow(t) {
  const li = document.createElement('li');
  const income = t.type === 'Income';
  li.className = 'tx ' + (income ? 'income' : 'expense');

  const ico = document.createElement('div');
  ico.className = 'ico';
  ico.textContent = income ? '↓' : '↑';

  const d = document.createElement('div');
  d.className = 'd';
  const title = document.createElement('b');
  title.textContent = t.description;
  const when = document.createElement('small');
  when.textContent = new Date(t.timestamp).toLocaleString('de-DE', { dateStyle: 'medium', timeStyle: 'short' });
  d.append(title, when);

  const amt = document.createElement('div');
  amt.className = 'amt';
  amt.textContent = (income ? '+' : '−') + fmt(t.amount);

  const del = document.createElement('button');
  del.className = 'del';
  del.type = 'button';
  del.textContent = 'Löschen';
  del.addEventListener('click', () => {
    if (!del.classList.contains('armed')) {
      del.classList.add('armed');
      del.textContent = 'Sicher?';
      setTimeout(() => { del.classList.remove('armed'); del.textContent = 'Löschen'; }, 3000);
      return;
    }
    removeTx(t.id);
  });

  li.append(ico, d, amt, del);
  return li;
}

async function loadList() {
  const q = new URLSearchParams();
  if ($('from').value) q.set('from', $('from').value);
  if ($('to').value) q.set('to', $('to').value);
  if ($('ftype').value) q.set('type', $('ftype').value);
  const r = await api('/api/transactions?' + q);

  const ul = $('txList');
  ul.replaceChildren(...r.items.map(buildRow));
  $('empty').hidden = r.items.length > 0;
  $('listSummary').textContent = r.count
    ? `${r.count} Buchung${r.count === 1 ? '' : 'en'} · Saldo im Zeitraum ${fmt(r.balance)}`
    : '';
}

async function refresh() {
  render(await api('/api/state'));
  if (!state.needsSetup) await loadList();
}

/* ---------- Aktionen ---------- */
async function removeTx(id) {
  try {
    const r = await api('/api/transactions/' + encodeURIComponent(id), { method: 'DELETE' });
    render(r.state);
    await loadList();
    toast('Buchung gelöscht');
  } catch (e) { fail(e); }
}

$('setupForm').addEventListener('submit', async (ev) => {
  ev.preventDefault();
  const amount = parseAmount($('setupAmount').value);
  if (!(amount > 0)) return toast('Bitte einen gültigen Betrag eingeben.', 'error');
  try {
    render(await api('/api/setup', { method: 'POST', body: { amount } }));
    await loadList();
    toast('Budget gespeichert');
  } catch (e) { fail(e); }
});

$('txForm').addEventListener('submit', async (ev) => {
  ev.preventDefault();
  const type = document.querySelector('input[name="type"]:checked').value;
  const amount = parseAmount($('amount').value);
  if (!(amount > 0)) return toast('Bitte einen gültigen Betrag eingeben.', 'error');
  $('txBtn').disabled = true;
  try {
    const r = await api('/api/transactions', {
      method: 'POST',
      body: { type, description: $('desc').value, amount },
    });
    render(r.state);
    await loadList();
    $('desc').value = '';
    $('amount').value = '';
    if (type === 'Expense') sadSmiley(amount);
    else toast('Einnahme gebucht');
    if (r.warning) {
      toast(r.warning.isExhausted
        ? 'Budget aufgebraucht!'
        : `Achtung: nur noch ${pct.format(r.warning.remainingPercent)} % Budget übrig.`, 'error');
    }
  } catch (e) { fail(e); }
  finally { $('txBtn').disabled = false; }
});

$('topupForm').addEventListener('submit', async (ev) => {
  ev.preventDefault();
  const amount = parseAmount($('topup').value);
  if (!(amount > 0)) return toast('Bitte einen gültigen Betrag eingeben.', 'error');
  try {
    const r = await api('/api/budget/topup', { method: 'POST', body: { amount } });
    render(r.state);
    $('topup').value = '';
    moneyRain(false, r.added);
  } catch (e) { fail(e); }
});

$('bonusBtn').addEventListener('click', async () => {
  try {
    const r = await api('/api/budget/bonus', { method: 'POST' });
    render(r.state);
    moneyRain(true, r.added);
  } catch (e) { fail(e); }
});

$('thrForm').addEventListener('submit', async (ev) => {
  ev.preventDefault();
  const percent = parseAmount($('thr').value);
  if (!(percent > 0 && percent < 100)) return toast('Warnschwelle muss zwischen 1 und 99 liegen.', 'error');
  try {
    render(await api('/api/budget/threshold', { method: 'PUT', body: { percent } }));
    toast('Warnschwelle gespeichert');
  } catch (e) { fail(e); }
});

for (const id of ['from', 'to', 'ftype']) {
  $(id).addEventListener('change', () => loadList().catch(fail));
}
$('resetFilter').addEventListener('click', () => {
  $('from').value = ''; $('to').value = ''; $('ftype').value = '';
  loadList().catch(fail);
});

/* ---------- Effekte ---------- */
const SAD_SVG = `
<svg viewBox="0 0 200 200" xmlns="http://www.w3.org/2000/svg">
  <defs>
    <radialGradient id="sg" cx="40%" cy="32%" r="75%">
      <stop offset="0" stop-color="#ffe98a"/><stop offset="1" stop-color="#f5b82e"/>
    </radialGradient>
  </defs>
  <circle cx="100" cy="100" r="92" fill="url(#sg)" stroke="#d9941a" stroke-width="4"/>
  <path d="M52 66 Q70 52 86 62" stroke="#6b4a10" stroke-width="7" stroke-linecap="round" fill="none"/>
  <path d="M148 66 Q130 52 114 62" stroke="#6b4a10" stroke-width="7" stroke-linecap="round" fill="none"/>
  <ellipse cx="70" cy="86" rx="9" ry="12" fill="#3a2a08"/>
  <ellipse cx="130" cy="86" rx="9" ry="12" fill="#3a2a08"/>
  <circle cx="73" cy="82" r="3" fill="#fff"/><circle cx="133" cy="82" r="3" fill="#fff"/>
  <path d="M66 146 Q100 114 134 146" stroke="#3a2a08" stroke-width="8" stroke-linecap="round" fill="none"/>
  <path class="tear" d="M70 102 C62 118 60 128 70 130 C80 128 78 118 70 102Z" fill="#5ab4ff" stroke="#2b86d6" stroke-width="2"/>
</svg>`;

function sadSmiley(amount) {
  const fx = $('fx');
  fx.querySelectorAll('.sad').forEach((n) => n.remove());
  const el = document.createElement('div');
  el.className = 'sad';
  el.innerHTML = SAD_SVG; // statisches, eigenes Markup
  const label = document.createElement('span');
  label.textContent = '−' + fmt(amount);
  el.appendChild(label);
  fx.appendChild(el);
  setTimeout(() => el.remove(), 2000);
}

function moneyRain(big, amount) {
  const fx = $('fx');
  let count = big ? 170 : 70;
  if (reduceMotion) count = Math.round(count / 4);
  const frag = document.createDocumentFragment();
  let longest = 0;

  for (let i = 0; i < count; i++) {
    const bill = document.createElement('div');
    bill.className = 'bill';
    const w = 54 + Math.random() * 46;
    const dur = 2.6 + Math.random() * 2.4;
    const delay = Math.random() * (big ? 2.4 : 1.4);
    longest = Math.max(longest, dur + delay);
    const spin = reduceMotion ? 0 : 1;
    bill.style.cssText = [
      `--x:${(Math.random() * 100).toFixed(1)}vw`,
      `--w:${w.toFixed(0)}px`,
      `--dur:${dur.toFixed(2)}s`,
      `--delay:${delay.toFixed(2)}s`,
      `--sway:${((Math.random() - 0.5) * 220).toFixed(0)}px`,
      `--r0:${((Math.random() - 0.5) * 80 * spin).toFixed(0)}deg`,
      `--r1:${((Math.random() - 0.5) * 900 * spin).toFixed(0)}deg`,
    ].join(';');
    const sym = document.createElement('i');
    sym.textContent = '€';
    bill.appendChild(sym);
    frag.appendChild(bill);
  }

  const banner = document.createElement('div');
  banner.className = 'fx-banner';
  banner.textContent = '+' + fmt(amount);
  frag.appendChild(banner);
  fx.appendChild(frag);

  setTimeout(() => {
    fx.querySelectorAll('.bill, .fx-banner').forEach((n) => n.remove());
  }, (longest + 0.5) * 1000);
}

/* ---------- Start ---------- */
$('today').textContent = new Date().toLocaleDateString('de-DE', { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' });
refresh().catch(fail);
