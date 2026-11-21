var manualCheck = document.getElementById('useManualNumberCheck');
var docNoInput = document.querySelector('[name="DocumentNo"]');
if (manualCheck) {
    manualCheck.addEventListener('change', function () {
        docNoInput.disabled = !this.checked;
        if (this.checked) docNoInput.focus();
    });
}
