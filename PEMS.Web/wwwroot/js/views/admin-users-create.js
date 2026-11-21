        var existingSelect = document.getElementById('existingEmployeeSelect');
        var newFields = document.getElementById('newEmployeeFields');

        function toggleNewEmployeeFields() {
            newFields.style.display = existingSelect.value ? 'none' : '';
        }

        existingSelect.addEventListener('change', toggleNewEmployeeFields);
        toggleNewEmployeeFields();
