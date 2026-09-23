mergeInto(LibraryManager.library, {
  $QuestBridgeNativeText: {
    session: null,
    position: function(options) {
      var session = QuestBridgeNativeText.session;
      if (!session || !session.element || session.id !== options.id) return;
      session.options = options;
      var canvas = Module.canvas;
      var bounds = canvas.getBoundingClientRect();
      var style = session.element.style;
      style.left = (bounds.left + options.x * bounds.width) + 'px';
      style.top = (bounds.top + options.y * bounds.height) + 'px';
      style.width = (options.width * bounds.width) + 'px';
      style.height = (options.height * bounds.height) + 'px';
      style.fontSize = Math.max(12, options.fontSize * bounds.height) + 'px';
      style.clipPath = 'inset(' + (options.clipTop * bounds.height) + 'px ' +
        (options.clipRight * bounds.width) + 'px ' + (options.clipBottom * bounds.height) +
        'px ' + (options.clipLeft * bounds.width) + 'px)';
    },
    snapshot: function(session) {
      var element = session.element;
      if (element) {
        session.value = element.value;
        session.start = element.selectionStart || 0;
        session.end = element.selectionEnd || 0;
      }
      return { id: session.id, value: session.value, start: session.start, end: session.end, closed: session.closed };
    },
    close: function() {
      var session = QuestBridgeNativeText.session;
      if (!session || session.closed) return;
      QuestBridgeNativeText.snapshot(session);
      session.closed = true;
      var element = session.element;
      session.element = null; // Blur caused by remove() must not recursively close.
      document.removeEventListener('pointerdown', session.outside, true);
      document.removeEventListener('wheel', session.outsideWheel, true);
      window.removeEventListener('blur', session.windowBlur);
      window.removeEventListener('scroll', session.windowScroll, true);
      window.removeEventListener('resize', session.resize);
      if (element) element.remove();
    }
  },

  QBText_Open__deps: ['$QuestBridgeNativeText'],
  QBText_Open: function(pointer) {
    var options = JSON.parse(UTF8ToString(pointer));
    QuestBridgeNativeText.close();
    var element = document.createElement('textarea');
    element.id = 'questbridge-native-text';
    element.value = options.value;
    element.readOnly = options.readOnly;
    element.placeholder = options.placeholder || '';
    element.spellcheck = !options.readOnly;
    element.autocomplete = 'off';
    element.setAttribute('autocapitalize', 'sentences');
    element.setAttribute('aria-label', options.readOnly ? 'Текст для выделения и копирования' : (options.placeholder || 'Введите текст'));
    if (options.limit > 0) element.maxLength = options.limit;
    element.style.cssText = 'position:fixed;z-index:2147483000;margin:0;padding:0;border:0;border-radius:2px;' +
      'box-sizing:border-box;resize:none;outline:2px solid rgba(120,195,250,.55);outline-offset:2px;' +
      'font-family:"Noto Sans","Segoe UI",Arial,sans-serif;font-weight:400;line-height:1.35;' +
      'white-space:pre-wrap;overflow-wrap:break-word;overflow:auto;touch-action:auto;' +
      'user-select:text;-webkit-user-select:text;caret-color:#17212a;';
    element.style.color = options.color;
    element.style.backgroundColor = options.background;
    var session = {
      id: options.id, element: element, options: options, value: options.value,
      start: options.start, end: options.end, closed: false, lastSent: '', composing: false
    };
    QuestBridgeNativeText.session = session;

    function normalise() {
      if (session.composing || !session.element || options.readOnly) return;
      var value = element.value;
      if (options.singleLine) value = value.replace(/[\r\n]+/g, ' ');
      if (options.limit > 0) value = value.substring(0, options.limit);
      if (element.value !== value) {
        var start = element.selectionStart, end = element.selectionEnd;
        element.value = value;
        element.setSelectionRange(Math.min(start, value.length), Math.min(end, value.length));
      }
    }
    element.addEventListener('compositionstart', function() { session.composing = true; });
    element.addEventListener('compositionend', function() { session.composing = false; normalise(); });
    element.addEventListener('input', normalise);
    element.addEventListener('blur', function() { if (!session.closed) QuestBridgeNativeText.close(); });
    element.addEventListener('keydown', function(event) {
      event.stopPropagation();
      if (event.isComposing || session.composing) return;
      if (event.key === 'Escape' || event.key === 'Tab' || (options.singleLine && event.key === 'Enter')) {
        event.preventDefault();
        QuestBridgeNativeText.close();
        Module.canvas.focus({ preventScroll: true });
      }
      // All other keys, including Ctrl/Cmd+C/V/X/A and Enter, use browser defaults.
    });
    element.addEventListener('keyup', function(event) { event.stopPropagation(); });
    element.addEventListener('keypress', function(event) { event.stopPropagation(); });
    element.addEventListener('wheel', function(event) {
      var canScroll = !options.readOnly && ((event.deltaY < 0 && element.scrollTop > 0) ||
        (event.deltaY > 0 && element.scrollTop + element.clientHeight < element.scrollHeight - 1));
      event.stopPropagation();
      if (canScroll) return;
      event.preventDefault();
      QuestBridgeNativeText.close();
      Module.canvas.dispatchEvent(new WheelEvent('wheel', {
        bubbles: true, cancelable: true, clientX: event.clientX, clientY: event.clientY,
        deltaX: event.deltaX, deltaY: event.deltaY, deltaZ: event.deltaZ, deltaMode: event.deltaMode
      }));
    }, { passive: false });
    session.outside = function(event) { if (event.target !== element) QuestBridgeNativeText.close(); };
    session.outsideWheel = function(event) { if (event.target !== element) QuestBridgeNativeText.close(); };
    session.windowBlur = function() { QuestBridgeNativeText.close(); };
    session.windowScroll = function(event) { if (event.target !== element) QuestBridgeNativeText.close(); };
    session.resize = function() { QuestBridgeNativeText.position(session.options); };
    document.addEventListener('pointerdown', session.outside, true);
    document.addEventListener('wheel', session.outsideWheel, true);
    window.addEventListener('blur', session.windowBlur);
    window.addEventListener('scroll', session.windowScroll, true);
    window.addEventListener('resize', session.resize);
    document.body.appendChild(element);
    QuestBridgeNativeText.position(options);
    element.focus({ preventScroll: true });
    element.setSelectionRange(options.start, options.end);
  },

  QBText_Position__deps: ['$QuestBridgeNativeText'],
  QBText_Position: function(pointer) {
    QuestBridgeNativeText.position(JSON.parse(UTF8ToString(pointer)));
  },

  QBText_SetValue__deps: ['$QuestBridgeNativeText'],
  QBText_SetValue: function(id, pointer) {
    var session = QuestBridgeNativeText.session;
    if (!session || session.id !== id || !session.element) return;
    var element = session.element, value = UTF8ToString(pointer);
    if (element.value === value) return;
    var start = element.selectionStart, end = element.selectionEnd;
    element.value = value;
    element.setSelectionRange(Math.min(start, value.length), Math.min(end, value.length));
  },

  QBText_Read__deps: ['$QuestBridgeNativeText'],
  QBText_Read: function(id, close) {
    var session = QuestBridgeNativeText.session;
    var json = '';
    if (session && session.id === id) {
      if (close) QuestBridgeNativeText.close();
      if (!session.composing || session.closed) {
        var current = JSON.stringify(QuestBridgeNativeText.snapshot(session));
        if (close || current !== session.lastSent) { json = current; session.lastSent = current; }
      }
    }
    var size = lengthBytesUTF8(json) + 1;
    var buffer = _malloc(size);
    stringToUTF8(json, buffer, size);
    return buffer; // IL2CPP frees the returned string buffer after marshaling.
  }
});
