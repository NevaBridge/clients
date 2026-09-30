// NevaBridge web chat for desktop hosts. The page renders the conversation; the host application
// performs every API call and answers over its message channel (protocol version 1).
'use strict';

(function () {
  // One adapter per host type; everything else in the page is host-neutral.
  function detectHost() {
    if (window.chrome && window.chrome.webview) {
      const webview = window.chrome.webview;
      return {
        post: function (message) { webview.postMessage(message); },
        listen: function (handler) { webview.addEventListener('message', function (event) { handler(event.data); }); },
      };
    }
    if (window.nevabridgeHost) {
      const electron = window.nevabridgeHost;
      return {
        post: function (message) { electron.postMessage(message); },
        listen: function (handler) { electron.onMessage(handler); },
      };
    }
    return null;
  }

  const host = detectHost();

  const el = {
    transcript: document.getElementById('transcript'),
    emptyHint: document.getElementById('empty-hint'),
    reportHead: document.getElementById('report-head'),
    reportTitle: document.getElementById('report-title'),
    reportStatus: document.getElementById('report-status'),
    report: document.getElementById('report'),
    reportFields: document.getElementById('report-fields'),
    outcome: document.getElementById('outcome'),
    error: document.getElementById('error'),
    composer: document.getElementById('composer'),
    draft: document.getElementById('draft'),
    send: document.getElementById('send'),
    submit: document.getElementById('submit-report'),
    newReport: document.getElementById('new-report'),
    reportTypes: Array.prototype.slice.call(document.querySelectorAll('#report-type [data-category]')),
    attach: document.getElementById('attach-file'),
    attachments: document.getElementById('attachments'),
  };

  // The report type selects the template, so it can only change before the first message.
  const defaultCategory = 'support_request';
  const state = {
    conversationId: null,
    report: null,
    busy: false,
    category: defaultCategory,
    features: {},
  };
  const pending = new Map();
  let nextRequestId = 1;

  // ---------- Host messaging ----------

  function request(type, payload) {
    const id = nextRequestId++;
    return new Promise(function (resolve, reject) {
      pending.set(id, { resolve: resolve, reject: reject });
      host.post(JSON.stringify(Object.assign({ id: id, type: type }, payload)));
    });
  }

  if (host) {
    host.listen(function (data) {
      let response;
      try {
        response = JSON.parse(data);
      } catch (e) {
        return;
      }
      const waiter = pending.get(response.id);
      if (!waiter) {
        return;
      }
      pending.delete(response.id);
      if (response.ok) {
        waiter.resolve(response.result);
      } else {
        waiter.reject(response.error);
      }
    });
  }

  // ---------- Rendering ----------

  function addMessage(role, text) {
    el.transcript.classList.remove('is-empty');
    el.emptyHint.hidden = true;
    const message = document.createElement('div');
    message.className = 'ds-msg';
    message.dataset.role = role;
    const label = document.createElement('span');
    label.className = 'ds-msg-role';
    if (role === 'assistant') {
      const mark = document.createElement('span');
      mark.className = 'ds-msg-mark';
      mark.textContent = '● ';
      label.appendChild(mark);
      label.appendChild(document.createTextNode('NevaBridge'));
    } else {
      label.textContent = 'You';
    }
    const bubble = document.createElement('div');
    bubble.className = 'ds-msg-bubble';
    bubble.textContent = text;
    message.appendChild(label);
    message.appendChild(bubble);
    el.transcript.appendChild(message);
    el.transcript.scrollTop = el.transcript.scrollHeight;
    return message;
  }

  function showThinking() {
    const message = addMessage('assistant', '');
    const bubble = message.querySelector('.ds-msg-bubble');
    bubble.classList.add('ds-msg-bubble-thinking');
    bubble.setAttribute('aria-label', 'NevaBridge is replying');
    for (let i = 0; i < 3; i++) {
      bubble.appendChild(document.createElement('i'));
    }
    return message;
  }

  const statusLabels = {
    reporting_in_progress: ['Draft', 'info'],
    submitted: ['Submitted', 'good'],
    assigned: ['Assigned', 'good'],
    engineering_in_progress: ['In progress', 'good'],
    pending_release: ['Awaiting release', 'good'],
    closed: ['Closed', ''],
  };

  function humanize(key) {
    const spaced = key.replace(/_/g, ' ').replace(/([a-z])([A-Z])/g, '$1 $2');
    return spaced.charAt(0).toUpperCase() + spaced.slice(1).toLowerCase();
  }

  function showReport(report, title) {
    state.report = report;
    if (!report) {
      el.reportHead.hidden = true;
      el.report.hidden = true;
      return;
    }
    const label = statusLabels[report.status] || [report.status, ''];
    el.reportHead.hidden = false;
    el.reportTitle.textContent = title || report.displayTitle || 'New report';
    el.reportStatus.textContent = label[0];
    el.reportStatus.parentElement.dataset.status = label[1];

    el.reportFields.replaceChildren();
    Object.keys(report.structured || {}).forEach(function (key) {
      const value = report.structured[key];
      if (!value || !value.trim()) {
        return;
      }
      const field = document.createElement('div');
      field.className = 'nb-report-field';
      const name = document.createElement('span');
      name.className = 'ds-mono';
      name.textContent = humanize(key);
      const text = document.createElement('div');
      text.className = 'nb-report-field-value';
      text.textContent = value;
      field.appendChild(name);
      field.appendChild(text);
      el.reportFields.appendChild(field);
    });
    el.report.hidden = el.reportFields.childElementCount === 0;
  }

  function showOutcome(prefix, connectors) {
    el.outcome.replaceChildren(document.createTextNode(prefix));
    Object.keys(connectors || {}).forEach(function (name) {
      const result = connectors[name];
      el.outcome.appendChild(document.createTextNode(' ' + name + ': '));
      if (result.status === 'success' && /^https:\/\//.test(result.ticket || '')) {
        const link = document.createElement('a');
        link.href = result.ticket;
        link.target = '_blank';
        link.rel = 'noopener';
        link.textContent = result.ticket;
        el.outcome.appendChild(link);
      } else {
        el.outcome.appendChild(document.createTextNode(
          result.status === 'success' ? result.ticket : 'not delivered (' + result.error + ')'));
      }
    });
    el.outcome.hidden = false;
  }

  function showError(error) {
    let text;
    if (error.status === 402) {
      text = 'NevaBridge has no credits left for this organization. Please contact your administrator.';
    } else if (error.status === 401 || error.status === 403) {
      text = 'NevaBridge refused the request (' + error.message + '). Check the stored API key and product.';
    } else if (error.retryable) {
      text = 'NevaBridge is busy or took too long to answer. Please try again in a moment.';
    } else {
      text = 'NevaBridge could not process the request: ' + error.message;
    }
    el.error.textContent = text;
    el.error.hidden = false;
  }

  function isFinished() {
    return state.report !== null && state.report.status !== 'reporting_in_progress';
  }

  function refreshControls() {
    const hasText = el.draft.value.trim().length > 0;
    el.send.disabled = !host || state.busy || isFinished() || !hasText;
    el.submit.disabled = !host || state.busy || !state.report || isFinished();
    el.newReport.disabled = state.busy;
    el.draft.disabled = !host || isFinished();
    el.composer.classList.toggle('is-sending', state.busy);
    el.attach.hidden = state.features.chatAttachments !== true;
    el.attach.disabled = !host || state.busy || state.conversationId === null || isFinished();
    const canChooseType = !!host && !state.busy && state.conversationId === null;
    el.reportTypes.forEach(function (option) {
      option.setAttribute('aria-pressed', String(option.dataset.category === state.category));
      option.disabled = !canChooseType;
    });
  }

  // ---------- Actions ----------

  async function send() {
    const text = el.draft.value.trim();
    if (!text || state.busy) {
      return;
    }
    el.error.hidden = true;
    el.draft.value = '';
    state.busy = true;
    refreshControls();
    const sent = addMessage('user', text);
    const thinking = showThinking();
    try {
      const turn = state.conversationId
        ? await request('appendMessage', { conversationId: state.conversationId, text: text })
        : await request('startConversation', { text: text, category: state.category });
      thinking.remove();
      state.conversationId = turn.assistantMessage.conversationId;
      addMessage('assistant', turn.assistantMessage.content);
      const report = turn.reports.find(function (r) { return r.status === 'reporting_in_progress'; })
        || turn.reports[0] || null;
      showReport(report, turn.displayTitle);
      if (turn.connectors) {
        showOutcome('The assistant judged the report complete and sent it.', turn.connectors);
      }
    } catch (error) {
      thinking.remove();
      sent.remove();
      el.draft.value = text;
      showError(error);
    } finally {
      state.busy = false;
      refreshControls();
      el.draft.focus();
    }
  }

  async function submitReport() {
    if (!state.report || state.busy) {
      return;
    }
    el.error.hidden = true;
    state.busy = true;
    refreshControls();
    try {
      const result = await request('submitReport', {
        conversationId: state.conversationId,
        reportId: state.report.id,
      });
      showReport(result.report, el.reportTitle.textContent);
      showOutcome('Thank you, the report was submitted.', result.connectors);
    } catch (error) {
      showError(error);
    } finally {
      state.busy = false;
      refreshControls();
    }
  }

  async function attachFile() {
    if (state.conversationId === null || state.busy) {
      return;
    }
    el.error.hidden = true;
    state.busy = true;
    refreshControls();
    try {
      const attachment = await request('attachFile', { conversationId: state.conversationId });
      if (attachment) {
        const item = document.createElement('li');
        item.textContent = attachment.fileName;
        el.attachments.appendChild(item);
        el.attachments.hidden = false;
      }
    } catch (error) {
      if (error.status === 403) {
        // The feature was switched off while the chat was open.
        state.features.chatAttachments = false;
        el.error.textContent = 'File attachments are not enabled for your organization.';
        el.error.hidden = false;
      } else {
        showError(error);
      }
    } finally {
      state.busy = false;
      refreshControls();
    }
  }

  async function loadFeatures() {
    try {
      state.features = (await request('getFeatures', {})) || {};
    } catch (error) {
      // Without the features, gated controls stay hidden; the chat itself still works.
      state.features = {};
    }
    refreshControls();
  }

  function startOver() {
    state.conversationId = null;
    el.attachments.replaceChildren();
    el.attachments.hidden = true;
    state.category = defaultCategory;
    showReport(null);
    el.transcript.querySelectorAll('.ds-msg').forEach(function (m) { m.remove(); });
    el.transcript.classList.add('is-empty');
    el.emptyHint.hidden = false;
    el.outcome.hidden = true;
    el.error.hidden = true;
    el.draft.value = '';
    refreshControls();
    el.draft.focus();
  }

  el.composer.addEventListener('submit', function (event) {
    event.preventDefault();
    send();
  });
  el.draft.addEventListener('keydown', function (event) {
    if (event.key === 'Enter' && !event.shiftKey && !event.isComposing) {
      event.preventDefault();
      send();
    }
  });
  el.draft.addEventListener('input', refreshControls);
  el.submit.addEventListener('click', submitReport);
  el.newReport.addEventListener('click', startOver);
  el.attach.addEventListener('click', attachFile);
  el.reportTypes.forEach(function (option) {
    option.addEventListener('click', function () {
      if (state.conversationId === null && !state.busy) {
        state.category = option.dataset.category;
        refreshControls();
      }
    });
  });

  if (!host) {
    el.error.textContent = 'This page runs inside a desktop application that connects it to NevaBridge.';
    el.error.hidden = false;
  } else {
    loadFeatures();
  }
  refreshControls();
  el.draft.focus();
})();
