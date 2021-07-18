function loadJSON(url, callback) {
  var req = new XMLHttpRequest();
  req.open('GET', url, true);
  req.responseType = "json";
  req.onload = function() {
    callback(req.status, req.response);
  };
  req.send();
}

function exchangeJSON(url, data, callback) {
  var req = new XMLHttpRequest();
  req.open('POST', url, true);
  req.setRequestHeader("Content-Type", "application/json");
  req.responseType = "json";
  req.onload = function() {
    callback(req.status, req.response);
  };
  req.send(JSON.stringify(data));
}

class PeriodicLoader {
  constructor(path, period) {
    this.path = path;
    this.period = period;
    this.callback = null;
    this.waiting = false;
  }

  start() {
    this.check();
    this._timer = setInterval(() => this.check(), this.period);
  }

  check() {
    let that = this;
    if (!that.waiting) {
      that.waiting = true;
      loadJSON(that.path, function(status, data){
        that.waiting = false;
        if (status == 200)
          that.callback(data);
      });
    }
  }

  stop() {
    this.callback = function(data){};
    this.waiting = false;
    clearTimeout(this._timer);
  }
}
