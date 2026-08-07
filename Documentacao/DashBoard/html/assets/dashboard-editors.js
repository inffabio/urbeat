(function () {
  "use strict";

  const DAYS = [
    { id: "monday", label: "Segunda-feira" },
    { id: "tuesday", label: "Terça-feira" },
    { id: "wednesday", label: "Quarta-feira" },
    { id: "thursday", label: "Quinta-feira" },
    { id: "friday", label: "Sexta-feira" },
    { id: "saturday", label: "Sábado" },
    { id: "sunday", label: "Domingo" }
  ];

  const INITIAL_SCHEDULE = {
    monday: { open: true, shifts: [{ start: "11:00", end: "14:30" }, { start: "18:00", end: "23:00" }] },
    tuesday: { open: true, shifts: [{ start: "11:00", end: "14:30" }, { start: "18:00", end: "23:00" }] },
    wednesday: { open: true, shifts: [{ start: "11:00", end: "14:30" }, { start: "18:00", end: "23:00" }] },
    thursday: { open: true, shifts: [{ start: "11:00", end: "14:30" }, { start: "18:00", end: "23:00" }] },
    friday: { open: true, shifts: [{ start: "11:00", end: "15:00" }, { start: "18:00", end: "00:00" }] },
    saturday: { open: true, shifts: [{ start: "11:00", end: "00:30" }] },
    sunday: { open: false, shifts: [] }
  };

  const ICONS = {
    trash: '<i class="bi bi-trash" aria-hidden="true"></i>',
    copy: '<i class="bi bi-copy" aria-hidden="true"></i>',
    close: '<i class="bi bi-x-lg" aria-hidden="true"></i>'
  };

  let toastTimer = null;

  function clone(value) {
    return JSON.parse(JSON.stringify(value));
  }

  function escapeHTML(value) {
    const node = document.createElement("div");
    node.textContent = value == null ? "" : String(value);
    return node.innerHTML;
  }

  function slugify(value) {
    return String(value)
      .normalize("NFD")
      .replace(/[\u0300-\u036f]/g, "")
      .toLowerCase()
      .replace(/[^a-z0-9]+/g, "-")
      .replace(/(^-|-$)/g, "");
  }

  function showToast(message, isError) {
    let toast = document.querySelector(".dashboard-toast");
    if (!toast) {
      toast = document.createElement("div");
      toast.className = "dashboard-toast";
      toast.setAttribute("role", "status");
      toast.setAttribute("aria-live", "polite");
      document.body.appendChild(toast);
    }
    window.clearTimeout(toastTimer);
    toast.textContent = message;
    toast.classList.toggle("error", Boolean(isError));
    toast.classList.add("visible");
    toastTimer = window.setTimeout(function () {
      toast.classList.remove("visible");
    }, 2800);
  }

  function minutesFromTime(value) {
    const parts = String(value || "00:00").split(":").map(Number);
    return parts[0] * 60 + parts[1];
  }

  function intervalFor(shift) {
    const start = minutesFromTime(shift.start);
    let end = minutesFromTime(shift.end);
    if (end <= start) end += 1440;
    return { start: start, end: end };
  }

  function isOvernight(shift) {
    return Boolean(shift.start && shift.end) && minutesFromTime(shift.end) <= minutesFromTime(shift.start);
  }

  function intervalsOverlap(firstShift, secondShift) {
    const first = intervalFor(firstShift);
    const second = intervalFor(secondShift);
    return [-1440, 0, 1440].some(function (offset) {
      const secondStart = second.start + offset;
      const secondEnd = second.end + offset;
      return Math.max(first.start, secondStart) < Math.min(first.end, secondEnd);
    });
  }

  function validateScheduleDay(dayData) {
    if (!dayData.open) return "";
    if (!dayData.shifts.length) return "Adicione pelo menos um turno ou feche este dia.";
    if (dayData.shifts.some(function (shift) { return !shift.start || !shift.end; })) {
      return "Preencha o início e o fim de todos os turnos.";
    }
    for (let first = 0; first < dayData.shifts.length; first += 1) {
      for (let second = first + 1; second < dayData.shifts.length; second += 1) {
        if (intervalsOverlap(dayData.shifts[first], dayData.shifts[second])) {
          return "Existem turnos sobrepostos neste dia.";
        }
      }
    }
    return "";
  }

  function formatMinutes(totalMinutes) {
    const normalized = ((totalMinutes % 1440) + 1440) % 1440;
    const hours = String(Math.floor(normalized / 60)).padStart(2, "0");
    const minutes = String(normalized % 60).padStart(2, "0");
    return hours + ":" + minutes;
  }

  function initScheduleEditor() {
    const scheduleCard = Array.from(document.querySelectorAll(".content-card")).find(function (card) {
      const heading = card.querySelector("h2");
      return heading && heading.textContent.includes("Horários de funcionamento");
    });
    if (!scheduleCard) return;

    let schedule = clone(INITIAL_SCHEDULE);
    let savedSchedule = clone(schedule);

    scheduleCard.classList.add("schedule-editor-card");
    scheduleCard.innerHTML = [
      '<div class="editor-toolbar">',
      '  <div>',
      '    <h2 class="h5 fw-bold mb-0">Horários de funcionamento</h2>',
      '    <p class="editor-help">Cadastre um ou mais turnos por dia. Horários que terminam após a meia-noite são identificados automaticamente.</p>',
      '  </div>',
      '  <div class="editor-toolbar-copy">',
      '    <span class="editor-summary" id="weeklyScheduleSummary"><i class="bi bi-clock-history"></i> 0h por semana</span>',
      '    <button class="btn-outline-app" type="button" data-schedule-action="restaurant-preset"><i class="bi bi-magic"></i> Modelo restaurante</button>',
      '  </div>',
      '</div>',
      '<div class="schedule-editor-v2" id="scheduleEditor"></div>',
      '<div class="schedule-footer-v2">',
      '  <span class="schedule-save-state" id="scheduleSaveState"><i class="bi bi-cloud-check"></i> Edite os turnos e salve as alterações</span>',
      '  <div class="d-flex gap-2">',
      '    <button class="btn-outline-app" type="button" data-schedule-action="cancel">Cancelar</button>',
      '    <button class="btn-primary-app btn-orange" type="button" data-schedule-action="save"><i class="bi bi-check-lg"></i> Salvar alterações</button>',
      '  </div>',
      '</div>'
    ].join("");

    const editor = scheduleCard.querySelector("#scheduleEditor");
    const summary = scheduleCard.querySelector("#weeklyScheduleSummary");
    const saveState = scheduleCard.querySelector("#scheduleSaveState");

    function renderSchedule() {
      editor.innerHTML = DAYS.map(function (day) {
        const data = schedule[day.id];
        const error = validateScheduleDay(data);
        const turns = data.open
          ? [
              '<div class="schedule-turns">',
              data.shifts.map(function (shift, index) {
                return [
                  '<div class="schedule-turn-row" data-turn-index="' + index + '">',
                  '  <label class="schedule-time-field">',
                  '    <span class="visually-hidden">Início do turno ' + (index + 1) + ' de ' + day.label + '</span>',
                  '    <input type="time" value="' + shift.start + '" data-schedule-action="time" data-day="' + day.id + '" data-index="' + index + '" data-field="start">',
                  '  </label>',
                  '  <span class="schedule-arrow">→</span>',
                  '  <label class="schedule-time-field">',
                  '    <span class="visually-hidden">Fim do turno ' + (index + 1) + ' de ' + day.label + '</span>',
                  '    <input type="time" value="' + shift.end + '" data-schedule-action="time" data-day="' + day.id + '" data-index="' + index + '" data-field="end">',
                  '  </label>',
                  '  <button class="schedule-remove" type="button" data-schedule-action="remove" data-day="' + day.id + '" data-index="' + index + '" aria-label="Remover turno ' + (index + 1) + ' de ' + day.label + '">' + ICONS.trash + '</button>',
                  '  <span class="schedule-next-day ' + (isOvernight(shift) ? "visible" : "") + '">Encerra no dia seguinte</span>',
                  '</div>'
                ].join("");
              }).join(""),
              '<button class="schedule-add" type="button" data-schedule-action="add" data-day="' + day.id + '"><i class="bi bi-plus-lg"></i> Adicionar turno</button>',
              '<p class="schedule-error ' + (error ? "visible" : "") + '">' + escapeHTML(error) + '</p>',
              '</div>'
            ].join("")
          : '<div class="schedule-closed-copy"><i class="bi bi-moon-stars me-2"></i> Loja fechada neste dia</div>';

        return [
          '<article class="schedule-day-v2 ' + (data.open ? "" : "is-closed") + " " + (error ? "has-error" : "") + '" data-schedule-day="' + day.id + '">',
          '  <div class="schedule-day-info">',
          '    <label class="schedule-toggle">',
          '      <input type="checkbox" data-schedule-action="toggle" data-day="' + day.id + '" ' + (data.open ? "checked" : "") + ' aria-label="Abrir ' + day.label + '">',
          '      <span aria-hidden="true"></span>',
          '    </label>',
          '    <div>',
          '      <div class="schedule-day-name">' + day.label + '</div>',
          '      <div class="schedule-day-meta">' + (data.open ? data.shifts.length + " " + (data.shifts.length === 1 ? "turno cadastrado" : "turnos cadastrados") : "Fechado") + '</div>',
          '    </div>',
          '  </div>',
          turns,
          '  <div class="schedule-day-actions">',
          '    <button class="schedule-copy" type="button" data-schedule-action="copy" data-day="' + day.id + '" ' + (!data.open ? "disabled" : "") + '>' + ICONS.copy + ' Copiar</button>',
          '  </div>',
          '</article>'
        ].join("");
      }).join("");
      updateScheduleSummary();
    }

    function updateScheduleSummary() {
      let total = 0;
      DAYS.forEach(function (day) {
        const data = schedule[day.id];
        if (!data.open) return;
        data.shifts.forEach(function (shift) {
          if (!shift.start || !shift.end) return;
          const interval = intervalFor(shift);
          total += interval.end - interval.start;
        });
      });
      const hours = Math.floor(total / 60);
      const minutes = total % 60;
      summary.innerHTML = '<i class="bi bi-clock-history"></i> ' + hours + "h" + (minutes ? " " + minutes + "min" : "") + " por semana";
    }

    function markScheduleChanged() {
      saveState.className = "schedule-save-state";
      saveState.innerHTML = '<i class="bi bi-pencil"></i> Alterações ainda não salvas';
      updateScheduleSummary();
    }

    function updateSingleDayValidation(dayId) {
      const row = editor.querySelector('[data-schedule-day="' + dayId + '"]');
      if (!row) return;
      const errorText = validateScheduleDay(schedule[dayId]);
      const errorNode = row.querySelector(".schedule-error");
      row.classList.toggle("has-error", Boolean(errorText));
      if (errorNode) {
        errorNode.textContent = errorText;
        errorNode.classList.toggle("visible", Boolean(errorText));
      }
    }

    editor.addEventListener("change", function (event) {
      const action = event.target.dataset.scheduleAction;
      const dayId = event.target.dataset.day;
      if (action === "toggle") {
        schedule[dayId].open = event.target.checked;
        if (event.target.checked && !schedule[dayId].shifts.length) {
          schedule[dayId].shifts = [{ start: "09:00", end: "18:00" }];
        }
        renderSchedule();
        markScheduleChanged();
      }
      if (action === "time") {
        const shift = schedule[dayId].shifts[Number(event.target.dataset.index)];
        shift[event.target.dataset.field] = event.target.value;
        const turn = event.target.closest(".schedule-turn-row");
        const overnight = turn.querySelector(".schedule-next-day");
        overnight.classList.toggle("visible", isOvernight(shift));
        updateSingleDayValidation(dayId);
        markScheduleChanged();
      }
    });

    editor.addEventListener("click", function (event) {
      const button = event.target.closest("[data-schedule-action]");
      if (!button) return;
      const action = button.dataset.scheduleAction;
      const dayId = button.dataset.day;
      if (action === "add") {
        const shifts = schedule[dayId].shifts;
        const last = shifts[shifts.length - 1];
        const startMinutes = last ? (minutesFromTime(last.end) + 60) % 1440 : 9 * 60;
        shifts.push({ start: formatMinutes(startMinutes), end: formatMinutes(startMinutes + 240) });
        renderSchedule();
        markScheduleChanged();
      }
      if (action === "remove") {
        schedule[dayId].shifts.splice(Number(button.dataset.index), 1);
        renderSchedule();
        markScheduleChanged();
      }
      if (action === "copy") {
        const sourceDay = DAYS.find(function (day) { return day.id === dayId; });
        const accepted = window.confirm("Copiar os turnos de " + sourceDay.label + " para todos os outros dias abertos?");
        if (!accepted) return;
        DAYS.forEach(function (day) {
          if (day.id !== dayId && schedule[day.id].open) {
            schedule[day.id].shifts = clone(schedule[dayId].shifts);
          }
        });
        renderSchedule();
        markScheduleChanged();
        showToast("Turnos copiados para os demais dias abertos.");
      }
    });

    scheduleCard.addEventListener("click", function (event) {
      const button = event.target.closest("[data-schedule-action]");
      if (!button || editor.contains(button)) return;
      const action = button.dataset.scheduleAction;
      if (action === "restaurant-preset") {
        DAYS.forEach(function (day) {
          if (schedule[day.id].open) {
            schedule[day.id].shifts = [{ start: "11:00", end: "14:30" }, { start: "18:00", end: "23:00" }];
          }
        });
        renderSchedule();
        markScheduleChanged();
        showToast("Modelo restaurante aplicado aos dias abertos.");
      }
      if (action === "cancel") {
        schedule = clone(savedSchedule);
        renderSchedule();
        saveState.className = "schedule-save-state";
        saveState.innerHTML = '<i class="bi bi-arrow-counterclockwise"></i> Alterações canceladas';
      }
      if (action === "save") {
        const invalidDay = DAYS.find(function (day) {
          return Boolean(validateScheduleDay(schedule[day.id]));
        });
        if (invalidDay) {
          editor.querySelector('[data-schedule-day="' + invalidDay.id + '"]').scrollIntoView({ behavior: "smooth", block: "center" });
          showToast("Revise os turnos destacados antes de salvar.", true);
          return;
        }
        if (!DAYS.some(function (day) { return schedule[day.id].open; })) {
          showToast("Abra pelo menos um dia para receber pedidos.", true);
          return;
        }
        savedSchedule = clone(schedule);
        saveState.className = "schedule-save-state is-success";
        saveState.innerHTML = '<i class="bi bi-cloud-check"></i> Horários salvos com sucesso';
        showToast("Horários de funcionamento atualizados.");
      }
    });

    renderSchedule();
  }

  const PRODUCT_TEMPLATES = {
    "Brasa Burger": {
      saleMode: "single",
      groups: [{
        name: "Adicionais",
        selection: "multiple",
        required: false,
        min: 0,
        max: 3,
        items: [{ name: "Bacon extra", price: 4 }, { name: "Cheddar extra", price: 3 }]
      }]
    },
    "Duplo Bacon": {
      saleMode: "size",
      sizes: [{ name: "Individual", price: 36.9 }, { name: "Triplo", price: 44.9 }],
      groups: [{
        name: "Ponto da carne",
        selection: "single",
        required: true,
        min: 1,
        max: 1,
        items: [{ name: "Ao ponto", price: 0 }, { name: "Bem passada", price: 0 }]
      }]
    },
    "Batata Frita": {
      saleMode: "size",
      sizes: [{ name: "Pequena", price: 12.9 }, { name: "Grande", price: 19.9 }],
      groups: [{
        name: "Molhos",
        selection: "multiple",
        required: false,
        min: 0,
        max: 2,
        items: [{ name: "Maionese verde", price: 2 }, { name: "Barbecue", price: 2 }]
      }]
    },
    "Combo Brasa": {
      saleMode: "single",
      groups: [{
        name: "Escolha a bebida",
        selection: "single",
        required: true,
        min: 1,
        max: 1,
        items: [{ name: "Coca-Cola", price: 0 }, { name: "Guaraná", price: 0 }, { name: "Sprite", price: 0 }]
      }]
    },
    "Refrigerante Lata": {
      saleMode: "single",
      groups: [{
        name: "Sabor",
        selection: "single",
        required: true,
        min: 1,
        max: 1,
        items: [{ name: "Coca-Cola", price: 0 }, { name: "Guaraná Antarctica", price: 0 }, { name: "Sprite", price: 0 }]
      }]
    },
    "Onion Rings": {
      saleMode: "size",
      sizes: [{ name: "Média", price: 14.9 }, { name: "Grande", price: 21.9 }],
      groups: []
    }
  };

  function formatPrice(value) {
    return new Intl.NumberFormat("pt-BR", { style: "currency", currency: "BRL" }).format(Number(value) || 0);
  }

  function parsePrice(value) {
    const normalized = String(value || "")
      .replace(/[^\d,.-]/g, "")
      .replace(/\./g, "")
      .replace(",", ".");
    return Number(normalized) || 0;
  }

  function modeLabel(mode) {
    return {
      single: "Produto único",
      size: "Por tamanho",
      weight: "Por peso",
      variable: "Preço variável"
    }[mode] || "Produto único";
  }

  function buildDefaultProductConfig(name, price) {
    const template = PRODUCT_TEMPLATES[name] || { saleMode: "single", groups: [] };
    const config = clone(template);
    config.saleMode = config.saleMode || "single";
    config.sizes = config.sizes || [{ name: "Padrão", price: price }];
    config.weight = config.weight || { pricePerKg: price, minimum: 0.1 };
    config.variable = config.variable || { minimumPrice: price, note: "Preço definido durante o atendimento" };
    config.groups = config.groups || [];
    return config;
  }

  function initProductEditor() {
    const productList = document.querySelector(".product-list");
    if (!productList || !document.querySelector(".menu-tabs a.active[href='cardapio-produtos.html']")) return;

    const productConfigs = new Map();
    let currentRow = null;
    let currentId = null;
    let editorConfig = null;
    let creatingProduct = false;

    const categories = Array.from(new Set(Array.from(productList.querySelectorAll(".product-row")).map(function (row) {
      return row.children[2] ? row.children[2].textContent.trim() : "";
    }).filter(Boolean)));

    const dialog = document.createElement("dialog");
    dialog.className = "dashboard-dialog";
    dialog.id = "productEditorDialog";
    dialog.innerHTML = [
      '<form class="dashboard-dialog-shell" id="productEditorForm">',
      '  <header class="dashboard-dialog-head">',
      '    <div><h2 id="productEditorTitle">Editar produto</h2><p>Atualize os dados, a forma de venda e os grupos de opções.</p></div>',
      '    <button class="dashboard-dialog-close" type="button" data-product-action="close" aria-label="Fechar">' + ICONS.close + '</button>',
      '  </header>',
      '  <div class="dashboard-dialog-body">',
      '    <section class="editor-section">',
      '      <div class="editor-section-head"><div><h3>Informações do produto</h3><p>Dados exibidos no cardápio.</p></div></div>',
      '      <div class="editor-grid">',
      '        <div class="editor-field"><label for="editProductName">Nome do produto</label><input id="editProductName" required maxlength="80"></div>',
      '        <div class="editor-field"><label for="editProductCategory">Categoria</label><select id="editProductCategory" required></select></div>',
      '        <div class="editor-field full"><label for="editProductDescription">Descrição</label><textarea id="editProductDescription" maxlength="220"></textarea></div>',
      '        <div class="editor-field"><label for="editProductPrice">Preço base</label><input id="editProductPrice" type="number" min="0" step="0.01" required></div>',
      '        <label class="editor-checkbox align-self-end mb-2"><input id="editProductActive" type="checkbox"> Produto ativo no cardápio</label>',
      '      </div>',
      '    </section>',
      '    <section class="editor-section">',
      '      <div class="editor-section-head"><div><h3>Forma de venda</h3><p>Defina como o cliente seleciona e paga por este produto.</p></div></div>',
      '      <div class="sale-mode-grid">',
      '        <label class="sale-mode-card"><input type="radio" name="editSaleMode" value="single"><span><strong>Produto único</strong><small>Um preço e uma apresentação.</small></span></label>',
      '        <label class="sale-mode-card"><input type="radio" name="editSaleMode" value="size"><span><strong>Por tamanho</strong><small>Preços diferentes por tamanho.</small></span></label>',
      '        <label class="sale-mode-card"><input type="radio" name="editSaleMode" value="weight"><span><strong>Por peso</strong><small>Valor calculado por quilo.</small></span></label>',
      '        <label class="sale-mode-card"><input type="radio" name="editSaleMode" value="variable"><span><strong>Preço variável</strong><small>Preço definido durante a venda.</small></span></label>',
      '      </div>',
      '      <div class="sale-detail-box" id="saleModeDetails"></div>',
      '    </section>',
      '    <section class="editor-section">',
      '      <div class="editor-section-head">',
      '        <div><h3>Grupos de opções</h3><p>Adicionais e escolhas disponíveis para este produto.</p></div>',
      '        <button class="btn-outline-app" type="button" data-product-action="add-group"><i class="bi bi-plus-lg"></i> Adicionar grupo</button>',
      '      </div>',
      '      <div id="productOptionGroups"></div>',
      '    </section>',
      '  </div>',
      '  <footer class="dashboard-dialog-footer">',
      '    <span class="dashboard-dialog-footer-note"><i class="bi bi-info-circle me-1"></i> As alterações são aplicadas ao salvar.</span>',
      '    <div class="dashboard-dialog-actions">',
      '      <button class="btn-outline-app" type="button" data-product-action="close">Cancelar</button>',
      '      <button class="btn-primary-app btn-orange" type="submit"><i class="bi bi-check-lg"></i> Salvar produto</button>',
      '    </div>',
      '  </footer>',
      '</form>'
    ].join("");
    document.body.appendChild(dialog);

    const form = dialog.querySelector("#productEditorForm");
    const groupsContainer = dialog.querySelector("#productOptionGroups");
    const saleModeDetails = dialog.querySelector("#saleModeDetails");

    function productDataFromRow(row) {
      const name = row.querySelector(".product-name").textContent.trim();
      const description = row.querySelector(".product-desc").textContent.trim();
      const category = row.children[2].textContent.trim();
      const price = parsePrice(row.children[3].textContent);
      const badge = row.children[4].querySelector(".badge-soft");
      return {
        name: name,
        description: description,
        category: category,
        price: price,
        active: badge ? badge.textContent.trim() === "Ativo" : true
      };
    }

    function registerRows() {
      Array.from(productList.querySelectorAll(".product-row")).forEach(function (row, index) {
        if (!row.dataset.editorId) {
          const product = productDataFromRow(row);
          row.dataset.editorId = "product-" + slugify(product.name) + "-" + index;
          productConfigs.set(row.dataset.editorId, buildDefaultProductConfig(product.name, product.price));
        }
        updateModeBadge(row);
      });
    }

    function updateModeBadge(row) {
      const config = productConfigs.get(row.dataset.editorId);
      if (!config) return;
      let badge = row.querySelector(".product-mode-badge");
      if (!badge) {
        badge = document.createElement("span");
        badge.className = "product-mode-badge";
        row.querySelector(".product-desc").insertAdjacentElement("afterend", badge);
      }
      badge.innerHTML = '<i class="bi bi-tag"></i> ' + modeLabel(config.saleMode) + " · " + config.groups.length + " " + (config.groups.length === 1 ? "grupo" : "grupos");
    }

    function renderCategoryOptions(selected) {
      const allCategories = categories.includes(selected) || !selected ? categories : categories.concat(selected);
      dialog.querySelector("#editProductCategory").innerHTML = allCategories.map(function (category) {
        return '<option value="' + escapeHTML(category) + '" ' + (category === selected ? "selected" : "") + ">" + escapeHTML(category) + "</option>";
      }).join("");
    }

    function renderSaleModeDetails() {
      if (editorConfig.saleMode === "single") {
        saleModeDetails.innerHTML = '<div class="sale-detail-note"><i class="bi bi-check-circle text-success"></i><span>O produto utiliza o preço base informado acima. Você ainda pode adicionar grupos de opções e adicionais.</span></div>';
        return;
      }
      if (editorConfig.saleMode === "size") {
        saleModeDetails.innerHTML = [
          '<div class="sale-rows">',
          editorConfig.sizes.map(function (size, index) {
            return '<div class="sale-row"><input value="' + escapeHTML(size.name) + '" data-size-field="name" data-index="' + index + '" aria-label="Nome do tamanho"><input type="number" min="0" step=".01" value="' + Number(size.price).toFixed(2) + '" data-size-field="price" data-index="' + index + '" aria-label="Preço do tamanho"><button class="editor-mini-remove" type="button" data-product-action="remove-size" data-index="' + index + '" aria-label="Remover tamanho">' + ICONS.trash + "</button></div>";
          }).join(""),
          '</div><button class="editor-add-link" type="button" data-product-action="add-size"><i class="bi bi-plus-lg"></i> Adicionar tamanho</button>'
        ].join("");
        return;
      }
      if (editorConfig.saleMode === "weight") {
        saleModeDetails.innerHTML = [
          '<div class="editor-grid">',
          '  <div class="editor-field"><label for="weightPrice">Preço por quilo</label><input id="weightPrice" type="number" min="0" step=".01" value="' + Number(editorConfig.weight.pricePerKg).toFixed(2) + '"></div>',
          '  <div class="editor-field"><label for="weightMinimum">Peso mínimo (kg)</label><input id="weightMinimum" type="number" min=".01" step=".01" value="' + Number(editorConfig.weight.minimum).toFixed(2) + '"></div>',
          '</div>'
        ].join("");
        return;
      }
      saleModeDetails.innerHTML = [
        '<div class="editor-grid">',
        '  <div class="editor-field"><label for="variableMinimum">Preço mínimo</label><input id="variableMinimum" type="number" min="0" step=".01" value="' + Number(editorConfig.variable.minimumPrice).toFixed(2) + '"></div>',
        '  <div class="editor-field"><label for="variableNote">Orientação para a equipe</label><input id="variableNote" value="' + escapeHTML(editorConfig.variable.note) + '"></div>',
        '</div>'
      ].join("");
    }

    function renderGroups() {
      if (!editorConfig.groups.length) {
        groupsContainer.innerHTML = '<div class="text-center text-secondary small fw-semibold py-4 border rounded-4">Nenhum grupo cadastrado. Use “Adicionar grupo” para criar opções para este produto.</div>';
        return;
      }
      groupsContainer.innerHTML = editorConfig.groups.map(function (group, groupIndex) {
        return [
          '<article class="option-group-editor" data-group-index="' + groupIndex + '">',
          '  <div class="option-group-head">',
          '    <div class="editor-field"><label>Nome do grupo</label><input value="' + escapeHTML(group.name) + '" data-group-field="name"></div>',
          '    <div class="editor-field"><label>Tipo de seleção</label><select data-group-field="selection"><option value="single" ' + (group.selection === "single" ? "selected" : "") + '>Opção única</option><option value="multiple" ' + (group.selection === "multiple" ? "selected" : "") + '>Múltiplas opções</option></select></div>',
          '    <button class="btn-outline-danger-app" type="button" data-product-action="remove-group" data-group-index="' + groupIndex + '"><i class="bi bi-trash"></i> Remover</button>',
          '  </div>',
          '  <div class="option-group-meta">',
          '    <label><input type="checkbox" data-group-field="required" ' + (group.required ? "checked" : "") + '> Obrigatório</label>',
          '    <label class="option-limit">Mín. <input type="number" min="0" value="' + group.min + '" data-group-field="min"></label>',
          '    <label class="option-limit">Máx. <input type="number" min="1" value="' + group.max + '" data-group-field="max"></label>',
          '  </div>',
          '  <div class="option-items-editor">',
          group.items.map(function (item, itemIndex) {
            return '<div class="option-item-editor"><input value="' + escapeHTML(item.name) + '" data-item-field="name" data-item-index="' + itemIndex + '" aria-label="Nome da opção"><input type="number" min="0" step=".01" value="' + Number(item.price).toFixed(2) + '" data-item-field="price" data-item-index="' + itemIndex + '" aria-label="Preço adicional"><button class="editor-mini-remove" type="button" data-product-action="remove-item" data-group-index="' + groupIndex + '" data-item-index="' + itemIndex + '" aria-label="Remover opção">' + ICONS.trash + "</button></div>";
          }).join(""),
          '  </div>',
          '  <button class="editor-add-link" type="button" data-product-action="add-item" data-group-index="' + groupIndex + '"><i class="bi bi-plus-lg"></i> Adicionar opção</button>',
          '</article>'
        ].join("");
      }).join("");
    }

    function syncConfigFromDOM() {
      if (!editorConfig) return;
      if (editorConfig.saleMode === "size") {
        const rows = Array.from(saleModeDetails.querySelectorAll(".sale-row"));
        editorConfig.sizes = rows.map(function (row) {
          return {
            name: row.querySelector('[data-size-field="name"]').value.trim(),
            price: Number(row.querySelector('[data-size-field="price"]').value) || 0
          };
        });
      }
      if (editorConfig.saleMode === "weight" && dialog.querySelector("#weightPrice")) {
        editorConfig.weight.pricePerKg = Number(dialog.querySelector("#weightPrice").value) || 0;
        editorConfig.weight.minimum = Number(dialog.querySelector("#weightMinimum").value) || 0;
      }
      if (editorConfig.saleMode === "variable" && dialog.querySelector("#variableMinimum")) {
        editorConfig.variable.minimumPrice = Number(dialog.querySelector("#variableMinimum").value) || 0;
        editorConfig.variable.note = dialog.querySelector("#variableNote").value.trim();
      }
      const groupNodes = Array.from(groupsContainer.querySelectorAll(".option-group-editor"));
      editorConfig.groups = groupNodes.map(function (groupNode) {
        return {
          name: groupNode.querySelector('[data-group-field="name"]').value.trim(),
          selection: groupNode.querySelector('[data-group-field="selection"]').value,
          required: groupNode.querySelector('[data-group-field="required"]').checked,
          min: Number(groupNode.querySelector('[data-group-field="min"]').value) || 0,
          max: Number(groupNode.querySelector('[data-group-field="max"]').value) || 1,
          items: Array.from(groupNode.querySelectorAll(".option-item-editor")).map(function (itemNode) {
            return {
              name: itemNode.querySelector('[data-item-field="name"]').value.trim(),
              price: Number(itemNode.querySelector('[data-item-field="price"]').value) || 0
            };
          })
        };
      });
    }

    function openEditor(row) {
      creatingProduct = !row;
      currentRow = row;
      if (row) {
        const data = productDataFromRow(row);
        currentId = row.dataset.editorId;
        editorConfig = clone(productConfigs.get(currentId) || buildDefaultProductConfig(data.name, data.price));
        dialog.querySelector("#productEditorTitle").textContent = "Editar produto";
        dialog.querySelector("#editProductName").value = data.name;
        dialog.querySelector("#editProductDescription").value = data.description;
        dialog.querySelector("#editProductPrice").value = data.price.toFixed(2);
        dialog.querySelector("#editProductActive").checked = data.active;
        renderCategoryOptions(data.category);
      } else {
        currentId = "product-new-" + Date.now();
        editorConfig = buildDefaultProductConfig("", 0);
        dialog.querySelector("#productEditorTitle").textContent = "Novo produto";
        dialog.querySelector("#editProductName").value = "";
        dialog.querySelector("#editProductDescription").value = "";
        dialog.querySelector("#editProductPrice").value = "";
        dialog.querySelector("#editProductActive").checked = true;
        renderCategoryOptions(categories[0] || "");
      }
      const modeRadio = dialog.querySelector('input[name="editSaleMode"][value="' + editorConfig.saleMode + '"]');
      if (modeRadio) modeRadio.checked = true;
      renderSaleModeDetails();
      renderGroups();
      dialog.showModal();
      window.setTimeout(function () { dialog.querySelector("#editProductName").focus(); }, 0);
    }

    dialog.addEventListener("change", function (event) {
      if (event.target.name === "editSaleMode") {
        syncConfigFromDOM();
        editorConfig.saleMode = event.target.value;
        renderSaleModeDetails();
      }
    });

    dialog.addEventListener("click", function (event) {
      const button = event.target.closest("[data-product-action]");
      if (!button) return;
      const action = button.dataset.productAction;
      if (action === "close") dialog.close();
      if (action === "add-size") {
        syncConfigFromDOM();
        editorConfig.sizes.push({ name: "Novo tamanho", price: Number(dialog.querySelector("#editProductPrice").value) || 0 });
        renderSaleModeDetails();
      }
      if (action === "remove-size") {
        syncConfigFromDOM();
        editorConfig.sizes.splice(Number(button.dataset.index), 1);
        if (!editorConfig.sizes.length) editorConfig.sizes.push({ name: "Padrão", price: 0 });
        renderSaleModeDetails();
      }
      if (action === "add-group") {
        syncConfigFromDOM();
        editorConfig.groups.push({
          name: "Novo grupo",
          selection: "single",
          required: false,
          min: 0,
          max: 1,
          items: [{ name: "Nova opção", price: 0 }]
        });
        renderGroups();
      }
      if (action === "remove-group") {
        syncConfigFromDOM();
        editorConfig.groups.splice(Number(button.dataset.groupIndex), 1);
        renderGroups();
      }
      if (action === "add-item") {
        syncConfigFromDOM();
        editorConfig.groups[Number(button.dataset.groupIndex)].items.push({ name: "Nova opção", price: 0 });
        renderGroups();
      }
      if (action === "remove-item") {
        syncConfigFromDOM();
        editorConfig.groups[Number(button.dataset.groupIndex)].items.splice(Number(button.dataset.itemIndex), 1);
        renderGroups();
      }
    });

    form.addEventListener("submit", function (event) {
      event.preventDefault();
      syncConfigFromDOM();
      const name = dialog.querySelector("#editProductName").value.trim();
      const description = dialog.querySelector("#editProductDescription").value.trim();
      const category = dialog.querySelector("#editProductCategory").value;
      const price = Number(dialog.querySelector("#editProductPrice").value);
      const active = dialog.querySelector("#editProductActive").checked;

      if (!name || !category || !Number.isFinite(price) || price < 0) {
        showToast("Preencha nome, categoria e um preço válido.", true);
        return;
      }
      const invalidGroup = editorConfig.groups.find(function (group) {
        return !group.name || !group.items.length || group.items.some(function (item) { return !item.name; }) || group.max < group.min;
      });
      if (invalidGroup) {
        showToast("Revise o nome, os limites e as opções dos grupos.", true);
        return;
      }

      if (creatingProduct) {
        const row = document.createElement("div");
        row.className = "product-row";
        row.dataset.editorId = currentId;
        row.innerHTML = [
          '<img alt="' + escapeHTML(name) + '" class="product-thumb" src="assets/burger.png">',
          '<div><div class="product-name">' + escapeHTML(name) + '</div><div class="product-desc">' + escapeHTML(description || "Produto cadastrado no cardápio.") + '</div></div>',
          '<div><i class="bi bi-shop me-2"></i>' + escapeHTML(category) + '</div>',
          '<strong>' + formatPrice(price) + '</strong>',
          '<div><span class="badge-soft ' + (active ? "badge-green" : "badge-orange") + '">' + (active ? "Ativo" : "Inativo") + '</span></div>',
          '<div class="action-buttons"><button class="btn-outline-app" type="button"><i class="bi bi-pencil"></i> Editar</button><button class="btn-outline-danger-app" type="button"><i class="bi bi-trash"></i> Excluir</button></div>'
        ].join("");
        productList.prepend(row);
        currentRow = row;
        productConfigs.set(currentId, clone(editorConfig));
        updateProductMetrics(1);
      } else {
        currentRow.querySelector(".product-name").textContent = name;
        currentRow.querySelector(".product-desc").textContent = description || "Produto cadastrado no cardápio.";
        currentRow.children[2].innerHTML = '<i class="bi bi-shop me-2"></i>' + escapeHTML(category);
        currentRow.children[3].textContent = formatPrice(price);
        currentRow.children[4].innerHTML = '<span class="badge-soft ' + (active ? "badge-green" : "badge-orange") + '">' + (active ? "Ativo" : "Inativo") + "</span>";
        productConfigs.set(currentId, clone(editorConfig));
      }
      updateModeBadge(currentRow);
      dialog.close();
      showToast("Produto, forma de venda e grupos de opções atualizados.");
    });

    function updateProductMetrics(delta) {
      const metric = document.querySelector(".metrics-grid.four .metric-value");
      if (metric) metric.textContent = String((Number(metric.textContent.trim()) || 0) + delta);
      const footerText = document.querySelector(".product-list + .p-3 .text-secondary");
      if (footerText) {
        const total = productList.querySelectorAll(".product-row").length;
        footerText.textContent = "Mostrando 1–" + total + " de " + total + " produtos";
      }
    }

    productList.addEventListener("click", function (event) {
      const row = event.target.closest(".product-row");
      if (!row) return;
      const editButton = event.target.closest(".btn-outline-app");
      const deleteButton = event.target.closest(".btn-outline-danger-app");
      if (editButton) openEditor(row);
      if (deleteButton) {
        const name = row.querySelector(".product-name").textContent.trim();
        if (window.confirm("Excluir o produto “" + name + "”?")) {
          productConfigs.delete(row.dataset.editorId);
          row.remove();
          updateProductMetrics(-1);
          showToast("Produto excluído.");
        }
      }
    });

    const newProductButton = document.querySelector(".filter-bar .btn-primary-app");
    if (newProductButton) {
      newProductButton.setAttribute("role", "button");
      newProductButton.addEventListener("click", function (event) {
        event.preventDefault();
        openEditor(null);
      });
    }

    dialog.addEventListener("click", function (event) {
      if (event.target === dialog) dialog.close();
    });

    registerRows();
  }

  document.addEventListener("DOMContentLoaded", function () {
    initScheduleEditor();
    initProductEditor();
  });
})();
