document.getElementById('implStatus').addEventListener('change', function () {
    document.getElementById('complianceWarning').hidden = !(this.value === 'ComplianceEvidenceAvailable');
});
