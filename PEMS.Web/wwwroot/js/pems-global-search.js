// ==============================================================================
// PEMS Global Intelligent Search — کامپوننت مشترک Autocomplete/Typeahead
//
// استفاده:
//   <input type="text" data-pems-search data-search-type="Equipment"
//          data-search-hidden="#equipmentId" placeholder="جستجوی تجهیز..." autocomplete="off" />
//   <input type="hidden" id="equipmentId" name="equipmentId" />
//
// رویداد سفارشی بعد از انتخاب:
//   input.addEventListener('pems:search-select', e => console.log(e.detail));
//
// طبق معماری فاز A: این تنها کامپوننت جستجوی سراسری در کل PEMS است - هیچ صفحه‌ای نباید
// دوباره از صفر Debounce/Keyboard Navigation/Highlight بنویسد.
// ==============================================================================

(function () {
    'use strict';

    const DEBOUNCE_MS = 300;
    const MIN_CHARS = 1; // طبق درخواست: "به محض تایپ اولین کاراکتر"
    const PAGE_SIZE = 10;
    const SEARCH_TIMEOUT_MS = 15000;

    function debounce(fn, delay) {
        let timer = null;
        return function (...args) {
            clearTimeout(timer);
            timer = setTimeout(() => fn.apply(this, args), delay);
        };
    }

    class PemsSearchBox {
        constructor(input) {
            this.input = input;
            this.entityType = input.dataset.searchType;
            this.hiddenInput = input.dataset.searchHidden ? document.querySelector(input.dataset.searchHidden) : null;
            this.projectId = input.dataset.searchProjectId || null;

            this.page = 1;
            this.query = '';
            this.items = [];
            this.hasMore = false;
            this.activeIndex = -1;
            this.abortController = null;

            this._buildPanel();
            this._bindEvents();
        }

        _buildPanel() {
            this.panel = document.createElement('div');
            this.panel.className = 'pems-search-panel';
            this.panel.style.display = 'none';

            // موقعیت‌دهی نسبی به Input - Input باید داخل یک والد position:relative باشد
            const wrapper = document.createElement('div');
            wrapper.className = 'pems-search-wrapper';
            wrapper.style.position = 'relative';
            this.input.parentNode.insertBefore(wrapper, this.input);
            wrapper.appendChild(this.input);
            wrapper.appendChild(this.panel);
        }

        _bindEvents() {
            const debouncedSearch = debounce(() => this._runSearch(true), DEBOUNCE_MS);

            this.input.addEventListener('input', () => {
                this.query = this.input.value.trim();

                // اگر کاربر دستی متن را پاک/عوض کرد، مقدار قبلی انتخاب‌شده دیگر معتبر نیست
                if (this.hiddenInput) this.hiddenInput.value = '';

                if (this.query.length < MIN_CHARS) {
                    this._closePanel();
                    return;
                }

                debouncedSearch();
            });

            this.input.addEventListener('keydown', (e) => this._handleKeydown(e));

            this.input.addEventListener('focus', () => {
                if (this.items.length > 0) this._showPanel();
            });

            document.addEventListener('click', (e) => {
                if (!this.panel.contains(e.target) && e.target !== this.input) {
                    this._closePanel();
                }
            });
        }

        _handleKeydown(e) {
            if (this.panel.style.display === 'none') return;

            const optionEls = Array.from(this.panel.querySelectorAll('.pems-search-item'));

            if (e.key === 'ArrowDown') {
                e.preventDefault();
                this.activeIndex = Math.min(this.activeIndex + 1, optionEls.length - 1);
                this._highlightActive(optionEls);
            } else if (e.key === 'ArrowUp') {
                e.preventDefault();
                this.activeIndex = Math.max(this.activeIndex - 1, 0);
                this._highlightActive(optionEls);
            } else if (e.key === 'Enter') {
                if (this.activeIndex >= 0 && this.items[this.activeIndex]) {
                    e.preventDefault();
                    this._selectItem(this.items[this.activeIndex]);
                } else if (this.query.length >= MIN_CHARS) {
                    // طبق «PEMS — Engineering Documents Density/Search Consolidation»: وقتی کاربر
                    // بدون انتخاب یک نتیجه‌ی مشخص Enter می‌زند، این‌جا فقط یک رویداد عمومی ساطع
                    // می‌شود - خودِ این کامپوننت مشترک هیچ اکشنی پیش‌فرض ندارد (نه Submit، نه
                    // Navigate). مصرف‌کننده‌ی هر صفحه تصمیم می‌گیرد با آن چه کند (مثلاً فیلتر
                    // کردن یک Grid). برای مصرف‌کننده‌های فعلی که این رویداد را گوش نمی‌دهند
                    // (اکثر Picker ها)، این تغییر کاملاً بی‌اثر است. e.preventDefault() این‌جا
                    // لازم است تا اگر Input داخل یک <form> باشد، Submit خودکار مرورگر با
                    // Submit دستیِ مصرف‌کننده تداخل/تکرار نکند.
                    e.preventDefault();
                    this.input.dispatchEvent(new CustomEvent('pems:search-enter', { detail: { query: this.query }, bubbles: true }));
                }
            } else if (e.key === 'Escape') {
                this._closePanel();
            }
        }

        _highlightActive(optionEls) {
            optionEls.forEach((el, i) => el.classList.toggle('active', i === this.activeIndex));
            const activeEl = optionEls[this.activeIndex];
            if (activeEl) activeEl.scrollIntoView({ block: 'nearest' });
        }

        async _runSearch(resetPage) {
            if (resetPage) {
                this.page = 1;
                this.items = [];
                this.activeIndex = -1;
            }

            if (this.abortController) this.abortController.abort();
            const controller = new AbortController();
            this.abortController = controller;

            // هیچ جستجویی نباید برای همیشه روی «در حال جستجو» بماند: پس از SEARCH_TIMEOUT_MS خطا نمایش داده می‌شود
            let timedOut = false;
            const timeoutId = setTimeout(() => { timedOut = true; controller.abort(); }, SEARCH_TIMEOUT_MS);

            this._renderLoading();

            const params = new URLSearchParams({
                type: this.entityType,
                q: this.query,
                page: String(this.page),
                pageSize: String(PAGE_SIZE)
            });
            if (this.projectId) params.set('projectId', this.projectId);

            try {
                const res = await fetch('/SearchApi?' + params.toString(), { signal: controller.signal });
                if (controller !== this.abortController) return; // پاسخ کهنه‌ی یک جستجوی قدیمی
                if (!res.ok) {
                    this._renderError();
                    return;
                }
                const data = await res.json();
                if (controller !== this.abortController) return;

                if (resetPage) {
                    this.items = data.items || [];
                } else {
                    this.items = this.items.concat(data.items || []);
                }

                this.hasMore = !!data.hasMore;
                this._render();
            } catch (err) {
                if (controller !== this.abortController && !timedOut) return;
                if (err.name !== 'AbortError' || timedOut) this._renderError();
            } finally {
                clearTimeout(timeoutId);
            }
        }

        _renderLoading() {
            this.panel.innerHTML = '<div class="pems-search-loading">در حال جستجو...</div>';
            this._showPanel();
        }

        _renderError() {
            this.panel.innerHTML = '<div class="pems-search-error">خطا در جستجو - دوباره تلاش کنید</div>';
        }

        _render() {
            if (this.items.length === 0) {
                this.panel.innerHTML = '<div class="pems-search-empty">نتیجه‌ای پیدا نشد</div>';
                this._showPanel();
                return;
            }

            const listHtml = this.items.map((item, i) => `
                <div class="pems-search-item${i === this.activeIndex ? ' active' : ''}" data-index="${i}">
                    <div class="pems-search-item-label">${item.highlightedLabel || item.label}</div>
                    ${item.subLabel ? `<div class="pems-search-item-sub">${this._escape(item.subLabel)}</div>` : ''}
                </div>
            `).join('');

            const loadMoreHtml = this.hasMore
                ? '<div class="pems-search-loadmore">بارگذاری بیشتر...</div>'
                : '';

            this.panel.innerHTML = listHtml + loadMoreHtml;

            this.panel.querySelectorAll('.pems-search-item').forEach((el) => {
                el.addEventListener('mousedown', (e) => {
                    e.preventDefault(); // جلوگیری از blur قبل از کلیک
                    const idx = parseInt(el.dataset.index, 10);
                    this._selectItem(this.items[idx]);
                });
            });

            const loadMoreEl = this.panel.querySelector('.pems-search-loadmore');
            if (loadMoreEl) {
                loadMoreEl.addEventListener('mousedown', (e) => {
                    e.preventDefault();
                    this.page++;
                    this._runSearch(false);
                });
            }

            this._showPanel();
        }

        _selectItem(item) {
            this.input.value = item.label;
            if (this.hiddenInput) this.hiddenInput.value = item.id;

            this._closePanel();

            this.input.dispatchEvent(new CustomEvent('pems:search-select', { detail: item, bubbles: true }));
        }

        _showPanel() {
            this.panel.style.display = 'block';
        }

        _closePanel() {
            this.panel.style.display = 'none';
            this.activeIndex = -1;
        }

        _escape(text) {
            const div = document.createElement('div');
            div.textContent = text;
            return div.innerHTML;
        }
    }

    function initAll() {
        document.querySelectorAll('[data-pems-search]').forEach((input) => {
            if (!input.dataset.pemsSearchInitialized) {
                new PemsSearchBox(input);
                input.dataset.pemsSearchInitialized = 'true';
            }
        });
    }

    // اجرای اولیه + پشتیبانی از محتوای دینامیک (مثلاً Modal هایی که بعداً به DOM اضافه می‌شوند)
    document.addEventListener('DOMContentLoaded', initAll);
    window.PemsGlobalSearch = { init: initAll };
})();
