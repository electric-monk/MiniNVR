function exchangeJSON(url, callback, senddata = null) {
    var req = new XMLHttpRequest();
    req.open(senddata ? 'POST' : 'GET', url, true);
    if (senddata)
        req.setRequestHeader("Content-Type", "application/json");
    req.responseType = "json";
    req.onload = function() {
        callback(req.status, req.response);
    };
    req.send(senddata ? JSON.stringify(senddata) : null);
}

function generateID() {
    let s = "";
    for (let i = 0; i < 10; i++) {
        let r = (Math.random() * 16) % 16 | 0;
        s += r.toString(16);
    }
    return s;
}

class DataMonitor {
    constructor(url, period) {
        this.url = url;
        this.handlers = [];
        this.data = null;
        this.timer = null;
        this.period = period;
    }

    _check() {
        if (!this.waiting) {
            this.waiting = true;
            let that = this;
            exchangeJSON(this.url, function(status, data) {
                that.waiting = false;
                if (status == 200)
                    that.update(data);
                else
                    that.error(status);
            });
        }
    }

    update(data) {
        this.data = data;
        this.handlers.forEach((item) => item(data));
    }

    error(status) {
        console.log("Got error " + status + " loading " + this.url);
    }

    subscribe(handler) {
        this.handlers.push(handler);
        if (!this.timer) {
            this.waiting = false;
            if (!this.data)
                this._check();
            this.timer = setInterval(() => this._check(), this.period);
        } else if (this.data) {
            handler(this.data);
        }
        var that = this;
        return () => {
            that.handlers = that.handlers.filter((item) => item != handler);
            if (that.timer && (that.handlers.length == 0)) {
                clearTimeout(that.timer);
                that.timer = null;
            }
        };
    }
}

class DataGetter {
    constructor(request, body, headers) {
        this.request = request;
        this.body = body;
        this.headers = headers;
    }

    start() {
        let use_method = this.body ? "POST" : "GET";
        let use_headers = this.headers ? this.headers : {};
        let that = this;
        fetch(this.request, {
            method: use_method,
            cache: 'no-cache',
            body: this.body ? JSON.stringify(this.body) : null,
            headers: use_headers
        }).then(response => {
            if (response.ok) {
                that.gotresponse(response)
                that.reader = response.body.getReader();
                that.next();
            } else {
                that.error();
            }
        });
    }

    _step(done, value) {
        if (value)
            this.gotdata(value);
        this.moreAvailable(!done);
    }

    gotresponse(response) {
        // Override point
    }

    gotdata(value) {
        // Override point
    }

    moreAvailable(isAvailable) {
        if (isAvailable)
            this.next();
    }

    next() {
        this.reader.read().then(({done, value}) => this._step(done, value));
    }

    error() {
        console.log("Error requesting " + this.request);
    }
}

class MediaHelper extends DataGetter {
    constructor(request, body, headers) {
        super(request, body, headers);
        this.mediaSource = new MediaSource();
        this.mediaSource.addEventListener("sourceopen", () => this._onOpen());
    }

    _onOpen() {
        URL.revokeObjectURL(this.mediaSource);
        this.pending = false;
        this.start();
    }

    gotresponse(value) {
        if (!this.sourceBuffer) {
            var mimetype = value.headers.get("X-MSE-Codec");
            if (!mimetype)
                console.log("Didn't get MSE MIME type from server, client will balk");
            this.sourceBuffer = this.mediaSource.addSourceBuffer(mimetype);
            this.sourceBuffer.mode = "sequence";
            this.sourceBuffer.addEventListener("updateend", () => this._updateEnd());
        }
    }

    gotdata(value) {
        this.sourceBuffer.appendBuffer(value);
    }

    moreAvailable(isAvailable) {
        this.pending = isAvailable;
    }

    _updateEnd() {
        if (this.pending)
            this.next();
        else
            this.mediaSource.endOfStream();
    }

    createVideo() {
        let video = document.createElement("video");
        video.src = URL.createObjectURL(this.mediaSource);
        return video;
    }
}
