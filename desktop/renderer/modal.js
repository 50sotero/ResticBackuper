(() => {
  'use strict';

  const api = window.rewindleModal;
  if (!api) return;

  const title = document.getElementById('title');
  const message = document.getElementById('message');
  const form = document.getElementById('form');
  const fields = document.getElementById('fields');
  const submit = document.getElementById('submit');
  const cancel = document.getElementById('cancel');
  let nonce = '';
  let options = {};
  let controls = [];

  function text(value, fallback = '') {
    return typeof value === 'string' ? value : fallback;
  }

  function addField(field, index) {
    const wrapper = document.createElement('div');
    wrapper.className = 'field';
    const name = text(field.name, `value${index}`);
    const label = document.createElement('label');
    label.htmlFor = `field-${index}`;
    label.textContent = text(field.label, name);
    const input = document.createElement(field.type === 'textarea' ? 'textarea' : field.type === 'select' ? 'select' : 'input');
    input.id = `field-${index}`;
    input.name = name;
    if (input.tagName === 'INPUT') input.type = field.type === 'password' ? 'password' : field.type === 'number' ? 'number' : field.type === 'time' ? 'time' : 'text';
    input.placeholder = text(field.placeholder);
    input.value = text(field.value);
    input.autocomplete = field.type === 'password' ? 'new-password' : 'off';
    if (field.required !== false) input.required = true;
    if (field.type === 'select') {
      for (const option of Array.isArray(field.options) ? field.options : []) {
        const item = document.createElement('option');
        item.value = text(option.value);
        item.textContent = text(option.label, item.value);
        input.append(item);
      }
      if (field.value) input.value = text(field.value);
    }
    wrapper.append(label, input);
    if (field.help) {
      const help = document.createElement('span');
      help.className = 'help';
      help.textContent = text(field.help);
      wrapper.append(help);
    }
    fields.append(wrapper);
    controls.push({ name, input });
  }

  function render(next) {
    options = next || {};
    nonce = text(options.nonce);
    title.textContent = text(options.title, 'Continue');
    message.textContent = text(options.message || options.prompt);
    submit.textContent = text(options.submitLabel, 'Continue');
    cancel.textContent = text(options.cancelLabel, 'Cancel');
    fields.replaceChildren();
    controls = [];
    const inputFields = Array.isArray(options.fields) && options.fields.length
      ? options.fields
      : [{ name: 'value', label: text(options.label, 'Value'), type: text(options.type, 'text'), value: text(options.value), placeholder: text(options.placeholder) }];
    for (const [index, field] of inputFields.entries()) addField(field || {}, index);
    const noInput = options.kind === 'showText' || options.kind === 'message';
    if (noInput) {
      for (const control of controls) control.input.required = false;
      fields.replaceChildren();
    }
    submit.hidden = Boolean(options.submitHidden || noInput);
    cancel.textContent = noInput ? text(options.submitLabel, 'Close') : text(options.cancelLabel, 'Cancel');
    requestAnimationFrame(() => (controls[0]?.input || cancel).focus());
  }

  form.addEventListener('submit', (event) => {
    event.preventDefault();
    if (!form.reportValidity()) return;
    const values = {};
    for (const control of controls) values[control.name] = control.input.value;
    if (options.kind === 'input' && controls.length === 1) api.submit(nonce, controls[0].input.value);
    else api.submit(nonce, values);
  });
  cancel.addEventListener('click', () => api.cancel(nonce));
  window.addEventListener('keydown', (event) => {
    if (event.key === 'Escape') {
      event.preventDefault();
      api.cancel(nonce);
    }
  });
  api.onInit(render);
})();
