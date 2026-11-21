// PEMS Dynamic Maintenance Organization — Phase 6C: Workforce Operations & UX Completion.
// طبق سند: بدون Drag & Drop، بدون منطق جابه‌جایی/تخصیص سمت کلاینت - این فایل فقط سه چیز را
// انجام می‌دهد: (۱) پرکردن Modal های مشترک از data-* دکمه‌ی کلیک‌شده (هم‌الگوی Phase 6B)،
// (۲) فیلتر وضعیت سمت روی همان جدول از پیش رندرشده (بدون درخواست تازه به سرور)،
// (۳) جستجوی کارمند سمت کلاینت روی یک <select> موجود (بدون کتابخانه‌ی تازه).
(function () {
    "use strict";

    function byId(id) { return document.getElementById(id); }

    // ---- Shared modals: prefill from the clicked trigger's data-* attributes ----

    var assignModal = byId("assignEmployeeModal");
    if (assignModal) {
        assignModal.addEventListener("show.bs.modal", function (e) {
            var btn = e.relatedTarget;
            byId("assignPositionId").value = btn.getAttribute("data-position-id");
            byId("assignPositionTitle").textContent = btn.getAttribute("data-position-title");
        });
    }

    var moveEmployeeModal = byId("moveEmployeeModal");
    if (moveEmployeeModal) {
        moveEmployeeModal.addEventListener("show.bs.modal", function (e) {
            var btn = e.relatedTarget;
            byId("moveEmployeeFromPositionId").value = btn.getAttribute("data-position-id");
            byId("moveEmployeeFromTitle").textContent = btn.getAttribute("data-position-title");
            byId("moveEmployeeCurrentOccupant").textContent = btn.getAttribute("data-employee-name");
        });
    }

    var movePositionModal = byId("movePositionModal");
    if (movePositionModal) {
        movePositionModal.addEventListener("show.bs.modal", function (e) {
            var btn = e.relatedTarget;
            byId("movePositionPositionId").value = btn.getAttribute("data-position-id");
            byId("movePositionTitle").textContent = btn.getAttribute("data-position-title");
        });
    }

    // طبق بند ۷ سند (Edit Position)
    var editPositionModal = byId("editPositionModal");
    if (editPositionModal) {
        editPositionModal.addEventListener("show.bs.modal", function (e) {
            var btn = e.relatedTarget;
            byId("editPositionId").value = btn.getAttribute("data-position-id");
            byId("editPositionTitle").value = btn.getAttribute("data-position-title") || "";
            byId("editPositionDescription").value = btn.getAttribute("data-position-description") || "";
            byId("editPositionSortOrder").value = btn.getAttribute("data-position-sortorder") || "0";
        });
    }

    // ---- Position status filter (client-side, no server round-trip) ----
    // طبق بند ۸ سند: VACANT/OCCUPIED/INACTIVE از همان Position.IsVacant/IsActive موجود می‌آید -
    // این‌جا فقط نمایش/عدم‌نمایش ردیف را کنترل می‌کند، هیچ وضعیت تازه‌ای اختراع نمی‌شود.
    var filterGroup = byId("positionStatusFilter");
    if (filterGroup) {
        var rows = document.querySelectorAll("#positionsTableBody tr");
        var emptyMsg = byId("positionsFilterEmpty");

        filterGroup.addEventListener("click", function (e) {
            var btn = e.target.closest("button[data-filter]");
            if (!btn) return;

            filterGroup.querySelectorAll("button").forEach(function (b) { b.classList.remove("active"); });
            btn.classList.add("active");

            var filter = btn.getAttribute("data-filter");
            var visibleCount = 0;
            rows.forEach(function (row) {
                var match = filter === "all" || row.getAttribute("data-status") === filter;
                row.hidden = !match;
                if (match) visibleCount++;
            });
            if (emptyMsg) emptyMsg.hidden = visibleCount !== 0;
        });
    }

    // ---- Employee search filter on existing <select> elements ----
    // طبق بند ۱۰ سند: بدون کتابخانه‌ی سنگین جدید - یک ورودی متن ساده که گزینه‌های همان
    // <select> موجود را فیلتر می‌کند؛ اعتبارسنجی سمت سرور دست‌نخورده می‌ماند (این فقط UX است).
    document.querySelectorAll(".pems-employee-search").forEach(function (input) {
        var select = document.querySelector(input.getAttribute("data-target"));
        if (!select) return;
        var allOptions = Array.prototype.slice.call(select.options);

        input.addEventListener("input", function () {
            var term = input.value.trim().toLowerCase();
            var currentValue = select.value;
            select.replaceChildren();
            allOptions.forEach(function (opt) {
                if (!term || opt.value === "" || opt.text.toLowerCase().indexOf(term) !== -1) {
                    select.appendChild(opt);
                }
            });
            if (allOptions.some(function (o) { return o.value === currentValue; }) &&
                Array.prototype.slice.call(select.options).some(function (o) { return o.value === currentValue; })) {
                select.value = currentValue;
            }
        });
    });
})();
