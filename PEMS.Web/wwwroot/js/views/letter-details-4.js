// the list's "Refer" row action lands here (Details#accReferrals): open the referrals section and scroll to it
document.addEventListener('DOMContentLoaded', function () {
    if (location.hash === '#accReferrals' || location.hash === '#accTimeline') {
        var el = document.getElementById(location.hash.slice(1));
        if (el && window.bootstrap) {
            window.bootstrap.Collapse.getOrCreateInstance(el, { toggle: false }).show();
            el.scrollIntoView({ block: 'center' });
        }
    }
});
