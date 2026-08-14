// Behaviour for the shared order-item fields (Views/Orders/_ItemFormFields.cshtml):
// which sections apply to the chosen item type, the barcode lookup, and the running total.
// Used by step 2 of the new-order wizard, by "add item to an existing order", and by the
// edit-item popup - where two copies of the fields are on the page at once.
//
// That last case is why nothing here looks anything up by element id. Every lookup is
// scoped to the container it was initialised with, via data-role attributes, so a second
// copy of the form cannot reach into the first one's inputs. Ids would collide the moment
// the popup opened, and the failure would be silent: the modal's controls would quietly
// drive the form underneath it.
(function () {
    function initItemForm(root) {
        if (!root || root.dataset.itemFormReady) return;
        root.dataset.itemFormReady = '1';

        const pick = role => root.querySelector('[data-role="' + role + '"]');

        const lookupFrameUrl = root.dataset.lookupFrameUrl;

        const itemType = pick('itemType');
        const frameSection = pick('frameSection');
        const extFrameSection = pick('extFrameSection');
        const lensSection = pick('lensSection');
        const doctorSection = pick('doctorSection');
        const barcodeInput = pick('barcodeInput');
        const framePriceInput = pick('framePriceInput');
        const extFrameInput = pick('extFrameInput');
        const lensDescInput = pick('lensDescInput');
        const lensSellInput = pick('lensSellInput');
        const previewFrame = pick('previewFrame');
        const previewLens = pick('previewLens');
        const previewTotal = pick('previewTotal');

        if (!itemType) return;

        function fmt(n) {
            return Number(n || 0).toLocaleString('ar-EG') + ' ج';
        }

        function updateTotal() {
            const f = itemType.value === 'LensesReplace' ? 0 : (+framePriceInput.value || 0);
            const l = itemType.value === 'FrameOnly' ? 0 : (+lensSellInput.value || 0);
            previewFrame.textContent = fmt(f);
            previewLens.textContent = fmt(l);
            previewTotal.textContent = fmt(f + l);
        }

        function onItemTypeChange() {
            const t = itemType.value;
            const showFrame = (t === 'FrameLenses' || t === 'FrameOnly');
            const showExtFrame = (t === 'LensesReplace');
            const showLens = (t !== 'FrameOnly');

            frameSection.style.display = showFrame ? 'block' : 'none';
            extFrameSection.style.display = showExtFrame ? 'block' : 'none';
            lensSection.style.display = showLens ? 'block' : 'none';
            doctorSection.style.display = showLens ? 'block' : 'none';

            barcodeInput.required = showFrame;
            framePriceInput.required = showFrame;
            // The customer's own frame is only ever a free-text note - never required.
            extFrameInput.required = false;
            lensDescInput.required = showLens;
            lensSellInput.required = showLens;

            updateTotal();
        }

        itemType.addEventListener('change', onItemTypeChange);
        framePriceInput.addEventListener('input', updateTotal);
        lensSellInput.addEventListener('input', updateTotal);
        onItemTypeChange();

        const extFrameContent = pick('extFrameContent');
        const extFrameArrow = pick('extFrameArrow');

        pick('extFrameToggleBtn').addEventListener('click', function () {
            const open = extFrameContent.classList.toggle('open');
            extFrameArrow.textContent = open ? '▲' : '▼';
        });

        // Don't hide a note that's already been written (validation re-render, or an item
        // opened for editing) behind a collapsed toggle.
        if (extFrameInput.value.trim()) {
            extFrameContent.classList.add('open');
            extFrameArrow.textContent = '▲';
        }

        const rxSection = pick('rxSection');
        const rxArrow = pick('rxArrow');

        pick('rxToggleBtn').addEventListener('click', function () {
            const open = rxSection.classList.toggle('open');
            rxArrow.textContent = open ? '▲' : '▼';
        });

        // Same reasoning as the external-frame note: a prescription already filled in
        // shouldn't be hidden behind a closed section when editing an existing item.
        if (root.querySelector('[data-rx-field]') &&
            Array.from(root.querySelectorAll('[data-rx-field]')).some(i => i.value.trim())) {
            rxSection.classList.add('open');
            rxArrow.textContent = '▲';
        }

        pick('searchFrameBtn').addEventListener('click', function () {
            const barcode = barcodeInput.value.trim();
            const resultBox = pick('frameResult');
            const errorBox = pick('frameError');
            resultBox.classList.remove('show');
            errorBox.classList.remove('show');

            if (!barcode) return;

            fetch(lookupFrameUrl + '?barcode=' + encodeURIComponent(barcode))
                .then(r => r.json())
                .then(data => {
                    if (data.found) {
                        pick('frameMeta').innerHTML =
                            '<span><b>' + (data.brand || '') + ' ' + (data.modelName || '') + '</b></span>' +
                            '<span>' + (data.color || '') + ' — ' + (data.size || '') + '</span>' +
                            '<span>السعر: <b>' + fmt(data.sellPrice) + '</b></span>' +
                            '<span>المتاح: <b>' + data.qtyAvailable + '</b></span>';
                        resultBox.classList.add('show');
                        framePriceInput.value = data.sellPrice;
                        updateTotal();
                    } else {
                        errorBox.textContent = data.message || 'الإطار غير موجود';
                        errorBox.classList.add('show');
                    }
                });
        });
    }

    // Exposed so a copy of the fields injected into a popup after page load can be wired
    // up too - the popup calls this once its HTML is in place.
    window.initItemForm = initItemForm;

    document.querySelectorAll('[data-item-form]').forEach(initItemForm);
})();
