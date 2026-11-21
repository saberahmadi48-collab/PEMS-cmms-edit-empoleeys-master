function pemsToggleTriggerFields() {
    var t = document.getElementById('triggerTypeSelect').value;
    var meter = (t === 'Meter' || t === 'Both'), cal = (t === 'Calendar' || t === 'Both');
    document.getElementById('meterTypeWrap').hidden = !(meter);
    document.getElementById('meterIntervalWrap').hidden = !(meter);
    document.getElementById('intervalDaysWrap').hidden = !(cal);
    document.getElementById('nextDueDateWrap').hidden = !(cal);
}
document.getElementById('triggerTypeSelect').addEventListener('change', pemsToggleTriggerFields);
pemsToggleTriggerFields();
