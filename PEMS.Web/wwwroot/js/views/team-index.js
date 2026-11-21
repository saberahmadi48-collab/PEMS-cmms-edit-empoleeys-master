// طبق «PEMS Dynamic Maintenance Organization — Phase 6A» بند ۶: بازکردن جزئیات یک تیم
// از نمودار سازمانی همین صفحه‌ی موجود را (با highlightTeamId) گسترش می‌دهد - مسیر
// جدیدی برای Team Details ساخته نشد.
(function () {
    var params = new URLSearchParams(window.location.search);
    var teamId = params.get("highlightTeamId");
    if (!teamId) return;
    var card = document.getElementById("team-card-" + teamId);
    if (!card) return;
    card.scrollIntoView({ behavior: "smooth", block: "center" });
    card.style.transition = "box-shadow .3s ease";
    card.style.boxShadow = "0 0 0 3px var(--pems-selection)";
    setTimeout(function () { card.style.boxShadow = ""; }, 2600);
})();
