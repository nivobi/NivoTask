window.NivoTaskIdle = {
    _ref: null,
    _last: 0,
    _threshold: 300,
    _interval: null,
    _bound: null,
    _vis: null,

    init: function (ref, thresholdSeconds) {
        this._ref = ref;
        this._threshold = thresholdSeconds || 300;
        this._last = Date.now();
        const self = this;
        this._bound = function () { self._last = Date.now(); };
        this._vis = function () { if (!document.hidden) self._last = Date.now(); };
        ['mousemove', 'keydown', 'pointerdown', 'focus'].forEach(function (e) {
            document.addEventListener(e, self._bound, true);
        });
        document.addEventListener('visibilitychange', this._vis, true);
        this._interval = setInterval(function () {
            const idle = (Date.now() - self._last) / 1000;
            if (idle >= self._threshold && self._ref) {
                self._ref.invokeMethodAsync('OnIdle', Math.floor(idle));
            }
        }, 30000);
    },

    bump: function () {
        this._last = Date.now();
    },

    dispose: function () {
        if (this._interval) clearInterval(this._interval);
        const self = this;
        if (this._bound) {
            ['mousemove', 'keydown', 'pointerdown', 'focus'].forEach(function (e) {
                document.removeEventListener(e, self._bound, true);
            });
        }
        if (this._vis) document.removeEventListener('visibilitychange', this._vis, true);
        this._interval = null;
        this._bound = null;
        this._vis = null;
        this._ref = null;
    }
};
