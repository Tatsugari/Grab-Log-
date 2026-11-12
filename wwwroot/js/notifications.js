(function () {
  const STORAGE_KEY = 'grablog_seen_notifs';

  function getSeen() {
    try {
      return JSON.parse(localStorage.getItem(STORAGE_KEY) || '[]');
    } catch {
      return [];
    }
  }

  function markSeen(id) {
    const seen = getSeen();
    if (!seen.includes(id)) {
      seen.push(id);
      localStorage.setItem(STORAGE_KEY, JSON.stringify(seen.slice(-200)));
    }
  }

  function showDesktop(title, body, tag) {
    if (!('Notification' in window)) return;
    if (Notification.permission !== 'granted') return;
    try {
      const n = new Notification(title, {
        body: body || '',
        icon: '/logo.png',
        badge: '/logo.png',
        tag: tag || ('grablog-' + Date.now()),
        requireInteraction: false
      });
      n.onclick = function () {
        window.focus();
        window.location.href = '/Notifications';
        n.close();
      };
      setTimeout(function () { try { n.close(); } catch (e) {} }, 8000);
    } catch (e) {
      console.warn('Notification failed', e);
    }
  }

  window.GrabLogNotify = {
    requestPermission: function () {
      if (!('Notification' in window)) return Promise.resolve('denied');
      if (Notification.permission === 'granted') return Promise.resolve('granted');
      if (Notification.permission === 'denied') return Promise.resolve('denied');
      return Notification.requestPermission();
    },
    show: showDesktop,
    showNewFromServer: function (items) {
      if (!items || !items.length) return;
      if (Notification.permission !== 'granted') return;
      const seen = getSeen();
      items.forEach(function (item) {
        const id = item.id || (item.title + item.message);
        if (seen.includes(id)) return;
        showDesktop(item.title, item.message, id);
        markSeen(id);
      });
    }
  };
})();
