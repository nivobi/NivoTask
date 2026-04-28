// Keyboard shortcut interop for NivoTask
window.NivoTaskKeyboard = {
    _dotNetRef: null,

    init: function (dotNetRef) {
        this._dotNetRef = dotNetRef;
        document.addEventListener('keydown', this._handler);
    },

    dispose: function () {
        document.removeEventListener('keydown', this._handler);
        this._dotNetRef = null;
    },

    _handler: function (e) {
        // Skip if user is typing in an input/textarea
        const tag = e.target.tagName;
        if (tag === 'INPUT' || tag === 'TEXTAREA' || e.target.isContentEditable) return;

        const ref = window.NivoTaskKeyboard._dotNetRef;
        if (!ref) return;

        ref.invokeMethodAsync('OnKeyPressed', e.key, e.ctrlKey || e.metaKey, e.shiftKey);
    }
};
