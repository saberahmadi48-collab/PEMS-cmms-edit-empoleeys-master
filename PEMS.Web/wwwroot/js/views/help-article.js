        var askButton = document.getElementById('helpAiAskBtn');
askButton.addEventListener('click', async function () {
            var question = document.getElementById('helpAiQuestion').value.trim();
            if (!question) return;
            var answerBox = document.getElementById('helpAiAnswer');
            answerBox.hidden = false;
            answerBox.replaceChildren(PemsTemplate.fill(PemsTemplate.clone('help-thinking'), { text: askButton.dataset.thinking }));

            var form = new FormData();
            form.append('articleCode', askButton.dataset.code);
            form.append('question', question);
            form.append('__RequestVerificationToken', document.querySelector('input[name="__RequestVerificationToken"]')?.value || '');

            try {
                var res = await fetch(askButton.dataset.url, { method: 'POST', body: form });
                var data = await res.json();
                answerBox.replaceChildren(PemsTemplate.fill(PemsTemplate.clone('help-answer'), { text: data.answer || '' }));
            } catch (e) {
                answerBox.replaceChildren(PemsTemplate.fill(PemsTemplate.clone('help-error'), { text: askButton.dataset.error }));
            }
        });
