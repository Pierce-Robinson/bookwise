// View or cancel a booking using the private link from the confirmation screen.
const $ = (id) => document.getElementById(id);
const params = new URLSearchParams(location.search);
const reference = params.get('ref');
const token = params.get('token');

async function api(path, options = {}) {
  const response = await fetch(path, { headers: { 'Content-Type': 'application/json' }, ...options });
  const body = await response.json().catch(() => null);
  if (!response.ok) throw new Error(body?.detail || body?.title || `Request failed (${response.status})`);
  return body;
}

function render(booking, business) {
  const when = new Intl.DateTimeFormat('en-ZA', { dateStyle: 'full', timeStyle: 'short', timeZone: business.timeZone });
  $('title').textContent = booking.status === 'Cancelled' ? 'Booking cancelled' : 'Booking details';
  $('ref').textContent = booking.reference;
  $('service').textContent = booking.serviceName;
  $('staff').textContent = booking.staffName;
  $('when').textContent = when.format(new Date(booking.startUtc));
  $('price').textContent = new Intl.NumberFormat('en-ZA', { style: 'currency', currency: 'ZAR' }).format(booking.price);
  $('status').textContent = booking.status;
  $('cancel-button').hidden = booking.status !== 'Confirmed';
  $('details').hidden = false;
}

async function init() {
  if (!reference || !token) {
    $('title').textContent = 'Booking link incomplete';
    return;
  }
  try {
    const [booking, business] = await Promise.all([
      api(`/api/public/bookings/${encodeURIComponent(reference)}?token=${encodeURIComponent(token)}`),
      api('/api/public/business'),
    ]);
    render(booking, business);

    $('cancel-button').addEventListener('click', async () => {
      if (!confirm('Cancel this booking?')) return;
      $('error').hidden = true;
      try {
        const cancelled = await api(`/api/public/bookings/${encodeURIComponent(reference)}/cancel`, {
          method: 'POST',
          body: JSON.stringify({ token }),
        });
        render(cancelled, business);
      } catch (error) {
        $('error').textContent = error.message;
        $('error').hidden = false;
      }
    });
  } catch (error) {
    $('title').textContent = 'Booking not found';
    $('error').textContent = error.message;
    $('error').hidden = false;
  }
}

init();
