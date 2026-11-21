        const chatWindow = document.getElementById('chatWindow');
        const form = document.getElementById('chatForm');
        const input = document.getElementById('messageInput');

        function addBubble(text, isUser) {
            const wrap = window.PemsTemplate.clone('chat-bubble');
            wrap.classList.add(isUser ? 'justify-content-end' : 'justify-content-start');
            const bubble = wrap.querySelector('[data-slot=bubble]');
            ('p-2 rounded ' + (isUser ? 'bg-primary text-white' : 'bg-white border')).split(' ').forEach(function (c) { bubble.classList.add(c); });
            bubble.style.maxWidth = '75%';
            bubble.innerText = text;
            chatWindow.appendChild(wrap);
            chatWindow.scrollTop = chatWindow.scrollHeight;
        }

        form.addEventListener('submit', async function (e) {
            e.preventDefault();
            const message = input.value.trim();
            if (!message) return;

            addBubble(message, true);
            input.value = '';
            addBubble('در حال فکر کردن... (روی سخت‌افزار ضعیف‌تر ممکن است تا یک دقیقه طول بکشد)', false);

            // طبق گزارش واقعی سخت‌افزار ضعیف: پیام انتظار را هر ۱۵ ثانیه به‌روزرسانی می‌کند تا
            // کاربر مطمئن شود سیستم گیر نکرده، فقط کند است
            let waitedSeconds = 0;
            const waitTimer = setInterval(function () {
                waitedSeconds += 15;
                const lastBubble = chatWindow.lastChild;
                if (lastBubble) {
                    lastBubble.querySelector('div').innerText = 'همچنان در حال پردازش... (' + waitedSeconds + ' ثانیه گذشته)';
                }
            }, 15000);

            const token = document.querySelector('input[name="__RequestVerificationToken"]').value;
            const body = new URLSearchParams();
            body.append('message', message);
            body.append('__RequestVerificationToken', token);

            try {
                const res = await fetch(form.dataset.askUrl, {
                    method: 'POST',
                    body: body
                });
                clearInterval(waitTimer);
                const data = await res.json();
                chatWindow.lastChild.remove();
                addBubble(data.answer, false);
            } catch (err) {
                clearInterval(waitTimer);
                chatWindow.lastChild.remove();
                addBubble('Error contacting the assistant. Please try again.', false);
            }
        });
