// Customer booking flow: service -> date/staff -> time slot -> details -> confirmation.
const state = { business: null, services: [], service: null, slot: null };

const $ = (id) => document.getElementById(id);

async function api(path, options = {}) {
  const response = await fetch(path, {
    headers: { 'Content-Type': 'application/json' },
    ...options,
  });
  const body = response.status === 204 ? null : await response.json().catch(() => null);
  if (!response.ok) {
    const fieldErrors = body?.errors ? Object.values(body.errors).flat().join(' ') : '';
    throw new Error(fieldErrors || body?.detail || body?.title || `Request failed (${response.status})`);
  }
  return body;
}

function showError(message) {
  $('error').textContent = message;
  $('error').hidden = !message;
}

const money = (amount) =>
  new Intl.NumberFormat('en-ZA', { style: 'currency', currency: 'ZAR' }).format(amount);

// Times are shown in the business's time zone, whatever the visitor's device is set to.
const timeFormat = () =>
  new Intl.DateTimeFormat('en-ZA', { hour: '2-digit', minute: '2-digit', timeZone: state.business.timeZone });
const dateTimeFormat = () =>
  new Intl.DateTimeFormat('en-ZA', { dateStyle: 'full', timeStyle: 'short', timeZone: state.business.timeZone });

function todayInBusinessZone() {
  // en-CA formats dates as YYYY-MM-DD, which is what <input type="date"> expects.
  return new Intl.DateTimeFormat('en-CA', { timeZone: state.business.timeZone }).format(new Date());
}

function renderServices() {
  const container = $('services');
  container.replaceChildren(...state.services.map((service) => {
    const button = document.createElement('button');
    button.type = 'button';
    button.className = 'option';
    button.setAttribute('role', 'radio');
    button.setAttribute('aria-checked', 'false');
    button.innerHTML = `<span><strong></strong><small></small></span><span class="price"></span>`;
    button.querySelector('strong').textContent = service.name;
    button.querySelector('small').textContent =
      `${service.durationMinutes} min${service.description ? ' · ' + service.description : ''}`;
    button.querySelector('.price').textContent = money(service.price);
    button.addEventListener('click', () => selectService(service, button));
    return button;
  }));
}

function selectService(service, button) {
  state.service = service;
  state.slot = null;
  document.querySelectorAll('#services .option').forEach((b) => b.setAttribute('aria-checked', 'false'));
  button.setAttribute('aria-checked', 'true');

  const staffSelect = $('staff');
  staffSelect.replaceChildren(new Option('Anyone available', ''),
    ...service.staff.map((s) => new Option(s.name, s.id)));

  $('step-time').hidden = false;
  $('step-details').hidden = true;
  loadSlots();
}

async function loadSlots() {
  showError('');
  const slots = $('slots');
  slots.textContent = 'Loading…';
  const params = new URLSearchParams({ serviceId: state.service.id, date: $('date').value });
  if ($('staff').value) params.set('staffId', $('staff').value);

  try {
    const result = await api(`/api/public/availability?${params}`);
    if (result.slots.length === 0) {
      slots.innerHTML = '<p class="muted">No times available on this day. Try another date.</p>';
      return;
    }
    const format = timeFormat();
    slots.replaceChildren(...result.slots.map((slot) => {
      const button = document.createElement('button');
      button.type = 'button';
      button.className = 'slot';
      button.setAttribute('aria-pressed', 'false');
      button.textContent = format.format(new Date(slot.startUtc));
      button.addEventListener('click', () => selectSlot(slot, button));
      return button;
    }));
  } catch (error) {
    slots.textContent = '';
    showError(error.message);
  }
}

function selectSlot(slot, button) {
  state.slot = slot;
  document.querySelectorAll('.slot').forEach((b) => b.setAttribute('aria-pressed', 'false'));
  button.setAttribute('aria-pressed', 'true');
  const staffName = $('staff').selectedOptions[0].text;
  $('summary').textContent =
    `${state.service.name} · ${dateTimeFormat().format(new Date(slot.startUtc))} · ${staffName} · ${money(state.service.price)}`;
  $('step-details').hidden = false;
  $('details-form').querySelector('input').focus();
}

async function submitBooking(event) {
  event.preventDefault();
  const form = event.target;
  if (!form.reportValidity()) return;

  const data = Object.fromEntries(new FormData(form));
  const button = $('book-button');
  button.disabled = true;
  showError('');
  try {
    const result = await api('/api/public/bookings', {
      method: 'POST',
      body: JSON.stringify({
        serviceId: state.service.id,
        staffId: $('staff').value ? Number($('staff').value) : null,
        start: state.slot.startUtc,
        customerName: data.customerName,
        customerEmail: data.customerEmail,
        customerPhone: data.customerPhone || null,
      }),
    });
    const booking = result.booking;
    $('confirmation').textContent =
      `${booking.serviceName} with ${booking.staffName} on ${dateTimeFormat().format(new Date(booking.startUtc))}. ` +
      `Your reference is ${result.reference}.`;
    $('manage-link').href = `/manage.html?ref=${encodeURIComponent(result.reference)}&token=${encodeURIComponent(result.cancelToken)}`;
    ['step-service', 'step-time', 'step-details'].forEach((id) => { $(id).hidden = true; });
    $('step-done').hidden = false;
  } catch (error) {
    showError(error.message);
    loadSlots(); // the slot may have just been taken; show what's still free
  } finally {
    button.disabled = false;
  }
}

async function init() {
  try {
    [state.business, state.services] = await Promise.all([api('/api/public/business'), api('/api/public/services')]);
    $('business-name').textContent = state.business.name;
    document.title = `Book at ${state.business.name}`;
    $('tz-note').textContent = `Times shown in ${state.business.timeZone.replace('_', ' ')} time.`;

    const date = $('date');
    date.min = todayInBusinessZone();
    date.value = date.min;
    date.addEventListener('change', loadSlots);
    $('staff').addEventListener('change', loadSlots);
    $('details-form').addEventListener('submit', submitBooking);
    renderServices();
  } catch (error) {
    showError(`Booking is unavailable right now: ${error.message}`);
  }
}

init();
