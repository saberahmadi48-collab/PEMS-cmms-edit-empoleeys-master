var manualNumCheck = document.getElementById('useManualNumberCheck');
var letterNoInput = document.querySelector('[name="letterNumber"]');
if (manualNumCheck) {
    manualNumCheck.addEventListener('change', function () { letterNoInput.disabled = !this.checked; });
}
