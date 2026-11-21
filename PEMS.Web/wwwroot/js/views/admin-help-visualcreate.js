function toggleSourceFields(type) {
    var isWorkflow = type === 'WorkflowDiagram';
    document.getElementById('workflowSourceFields').style.display = isWorkflow ? '' : 'none';
    document.getElementById('manualDefinitionField').style.display = isWorkflow ? 'none' : '';
}
function applyWorkflowSource(value) {
    var parts = value.split('|');
    document.querySelector('[name="SourceWorkflowModule"]').value = parts[0] || '';
    document.querySelector('[name="SourceWorkflowCategory"]').value = parts[1] || '';
}
document.addEventListener('DOMContentLoaded', function () {
    toggleSourceFields(document.querySelector('[name="VisualType"]').value);
});
