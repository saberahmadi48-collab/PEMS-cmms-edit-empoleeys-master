    function pemsToggleIssueFields() {
        var isExternal = document.getElementById('ext_unit').checked;
        var isDept = document.getElementById('dept_internal').checked;

        document.getElementById('issueDeptField').hidden = !(isDept);
        document.getElementById('issueExternalFields').hidden = !(isExternal);

        // فیلدهای نامه: برای بیرونی همیشه نشون داده بشه (اجباریه)، برای داخلی هم نشون داده
        // بشه ولی اختیاریه (سرویس خودش تشخیص می‌ده اگه موضوع خالی بود نامه نسازه)
        var showLetterFields = isExternal || isDept || document.getElementById('dcc_internal').checked;
        document.getElementById('issueLetterNumberField').hidden = !(showLetterFields);
        document.getElementById('issueSubjectField').hidden = !(showLetterFields);
        document.getElementById('issueBodyField').hidden = !(showLetterFields);

        // اگه بیرونیه، موضوع و گیرنده اجباری بشن
        var subjectInput = document.querySelector('#issueSubjectField input');
        var receiverInput = document.querySelector('#issueExternalFields input');
        if (subjectInput) subjectInput.required = isExternal;
        if (receiverInput) receiverInput.required = isExternal;
    }

    pemsToggleIssueFields();
