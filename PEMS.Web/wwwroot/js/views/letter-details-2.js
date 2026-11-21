    (function () {
        var loaded = false;
        var pickers = document.querySelectorAll('.employee-picker');

        function loadEmployeesOnce() {
            if (loaded) return;
            loaded = true;
            fetch('/Letter/GetEmployeesForPicker')
                .then(function (r) { return r.json(); })
                .then(function (list) {
                    pickers.forEach(function (sel) {
                        list.forEach(function (e) {
                            var opt = document.createElement('option');
                            opt.value = e.employeeId;
                            opt.textContent = e.name;
                            sel.appendChild(opt);
                        });
                    });
                });
        }

        pickers.forEach(function (sel) {
            sel.addEventListener('focus', loadEmployeesOnce, { once: true });
        });
    })();
