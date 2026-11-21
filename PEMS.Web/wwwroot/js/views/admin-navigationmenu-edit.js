// پیش‌نمایش آیکون هنگام تایپ (فقط کلاس‌های معتبر bi-*)
(function () {
    var input = document.getElementById('Icon'), preview = document.getElementById('iconPreview');
    if (!input || !preview) return;
    input.addEventListener('input', function () {
        preview.className = /^bi-[a-z0-9-]{1,50}$/.test(input.value) ? 'bi ' + input.value : 'bi';
    });
})();
