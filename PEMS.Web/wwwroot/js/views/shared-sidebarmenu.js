    // چرخش آیکون Chevron هنگام باز/بسته شدن هر بخش
    document.querySelectorAll('.nav-section-title').forEach(function (title) {
        var targetId = title.getAttribute('data-bs-target');
        var target = document.querySelector(targetId);
        var chevron = title.querySelector('.nav-section-chevron');

        target.addEventListener('show.bs.collapse', function () { chevron.classList.add('rotated'); });
        target.addEventListener('hide.bs.collapse', function () { chevron.classList.remove('rotated'); });

        // طبق «PEMS — Hover-Expandable Sidebar Navigation» بند Accessibility: این div ها
        // role="button" دارند ولی Div بومی با Enter/Space کلیک نمی‌شود - برخلاف <button>.
        // بدون این، تیتر بخش‌ها فقط با ماوس قابل باز/بسته شدن بودند.
        title.addEventListener('keydown', function (e) {
            if (e.key === 'Enter' || e.key === ' ') {
                e.preventDefault();
                title.click();
            }
        });
    });

    // طبق اصلاح ممیزی بصری (Critical Visual Correction): متن هر nav-link از قبل در HTML
    // در یک Span جداگانه Wrap نشده بود (برخلاف .nav-section-title که از قبل <span> دارد) -
    // بدون این، در حالت Icon Rail، خودِ Flex Item متن (نه فقط ظرف بیرونی) هرگز باریک‌تر از
    // عرض واقعیِ برچسبش نمی‌شد (رفتار پیش‌فرض min-width:auto در Flexbox) و در نیمه‌راه توسط
    // لبه‌ی نوار ۶۴px بریده می‌شد - دقیقاً همون بریدگی «Quick Set...» گزارش‌شده. این‌جا بدون
    // تغییر Razor Markup (که می‌شد ~۱۰۰ پیوند را دستی ویرایش کرد)، متن هر پیوند در یک
    // <span class="nav-label"> بسته‌بندی می‌شود تا CSS بتواند مستقیماً روی عرض/شفافیت خودِ
    // متن کنترل داشته باشد، نه فقط ظرف بیرونی‌اش.
    document.querySelectorAll('.pems-sidebar .nav-link').forEach(function (link) {
        if (link.querySelector('.nav-label')) return; // قبلاً Wrap شده - از اجرای دوباره جلوگیری می‌کند
        var label = window.PemsTemplate.clone('nav-label');
        Array.from(link.childNodes).forEach(function (node) {
            if (node.nodeType === 1 && node.tagName === 'I') return; // آیکون در جای خودش می‌ماند
            label.appendChild(node);
        });
        link.appendChild(label);
    });

    // طبق بند «Preserve tooltips for icon-only navigation»: به‌جای افزودن title به هر
    // پیوند/تیتر بخش به‌صورت دستی (~۱۰۰ مورد)، از همون متن قابل‌مشاهده‌ی موجود (بدون آیکون -
    // چون گلیف Bootstrap Icons از طریق CSS ::before است، نه textContent) به‌صورت خودکار
    // Tooltip بومی مرورگر ساخته می‌شود - برای هر آیتم فعلی و آینده، بدون نگهداری دستی.
    document.querySelectorAll('.pems-sidebar .nav-link, .pems-sidebar .nav-section-title').forEach(function (el) {
        if (!el.hasAttribute('title')) {
            var label = el.textContent.trim();
            if (label) el.setAttribute('title', label);
        }
    });
