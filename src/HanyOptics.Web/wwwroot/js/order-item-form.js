// Behaviour for the shared order-item fields (Views/Orders/_ItemFormFields.cshtml):
// which sections apply to the chosen item type, the barcode lookup, and the running
// total. Used by both step 2 of the new-order wizard and "add item to an existing order".
//
// Lives outside the views so the two screens can't drift apart in what they require or
// how they price an item. The one server-side value it needs - the LookupFrame URL -
// arrives as a data attribute, which is what keeps this file free of Razor.
(function () {
    const root = document.getElementById('itemFormFields');
    if (!root) return;

    const lookupFrameUrl = root.dataset.lookupFrameUrl;

    const itemType = document.getElementById('itemType');
    const frameSection = document.getElementById('frameSection');
    const extFrameSection = document.getElementById('extFrameSection');
    const lensSection = document.getElementById('lensSection');
    const doctorSection = document.getElementById('doctorSection');
    const barcodeInput = document.getElementById('barcodeInput');
    const framePriceInput = document.getElementById('framePriceInput');
    const extFrameInput = document.getElementById('extFrameInput');
    const lensDescInput = document.getElementById('lensDescInput');
    const lensSellInput = document.getElementById('lensSellInput');
    const previewFrame = document.getElementById('previewFrame');
    const previewLens = document.getElementById('previewLens');
    const previewTotal = document.getElementById('previewTotal');

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

    const extFrameContent = document.getElementById('extFrameContent');
    const extFrameArrow = document.getElementById('extFrameArrow');

    document.getElementById('extFrameToggleBtn').addEventListener('click', function () {
        const open = extFrameContent.classList.toggle('open');
        extFrameArrow.textContent = open ? '▲' : '▼';
    });

    // Don't hide a note that's already been written (validation re-render, or the user
    // coming back to this step) behind a collapsed toggle.
    if (extFrameInput.value.trim()) {
        extFrameContent.classList.add('open');
        extFrameArrow.textContent = '▲';
    }

    document.getElementById('rxToggleBtn').addEventListener('click', function () {
        const section = document.getElementById('rxSection');
        const arrow = document.getElementById('rxArrow');
        const open = section.classList.toggle('open');
        arrow.textContent = open ? '▲' : '▼';
    });

    document.getElementById('searchFrameBtn').addEventListener('click', function () {
        const barcode = barcodeInput.value.trim();
        const resultBox = document.getElementById('frameResult');
        const errorBox = document.getElementById('frameError');
        resultBox.classList.remove('show');
        errorBox.classList.remove('show');

        if (!barcode) return;

        fetch(lookupFrameUrl + '?barcode=' + encodeURIComponent(barcode))
            .then(r => r.json())
            .then(data => {
                if (data.found) {
                    document.getElementById('frameMeta').innerHTML =
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
})();
