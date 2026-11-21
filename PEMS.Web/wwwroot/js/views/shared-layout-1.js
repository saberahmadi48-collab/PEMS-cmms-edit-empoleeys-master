        async function loadNotifDropdown() {
            const res = await fetch('/Notification/Dropdown', { cache: 'no-store' });
            PemsTemplate.html(document.getElementById('notifDropdownBody'), await res.text());
        }

        function showLiveToast(message, isError) {
            const area = document.getElementById('liveToastArea');
            const el = PemsTemplate.fill(PemsTemplate.clone(isError ? 'toast-notif-error' : 'toast-notif'), { message: message });
            area.appendChild(el);
            new bootstrap.Toast(el, { delay: 6000 }).show();
        }

        document.addEventListener('DOMContentLoaded', function () {
            document.querySelectorAll('.toast').forEach(function (el) {
                new bootstrap.Toast(el).show();
            });

            const toggleBtn = document.getElementById('sidebarToggleBtn');
            const sidebar = document.querySelector('.pems-sidebar');
            if (toggleBtn && sidebar) {
                toggleBtn.addEventListener('click', function () {
                    if (window.innerWidth >= 992) {
                        sidebar.classList.toggle('pems-collapsed');
                        // طبق «PEMS — Hover-Expandable Sidebar Navigation»: وقتی نوار کاملاً
                        // پنهان می‌شود (نه فقط جمع به حالت Icon Rail)، محتوا دیگر فضایی برای
                        // آن رزرو نمی‌کند - این کلاس روی body برای همین تصمیم در CSS استفاده می‌شود.
                        document.body.classList.toggle('pems-sidebar-fully-hidden', sidebar.classList.contains('pems-collapsed'));
                    } else {
                        sidebar.classList.toggle('pems-opened');
                    }
                });
            }

            window.addEventListener('pageshow', function (event) {
                if (event.persisted) window.location.reload();
            });

            document.querySelectorAll('.culture-switch, .theme-switch').forEach(function (link) {
                link.addEventListener('click', function () {
                    document.documentElement.setAttribute('data-culture-switching', 'true');
                    document.body.style.visibility = 'hidden';
                });
            });
        });
