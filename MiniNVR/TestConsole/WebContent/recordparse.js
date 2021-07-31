function _colourForStorage(storage) {
    var hash = 0;
    for (var i = 0; i < storage.length; i++)
        hash = storage.charCodeAt(i) + ((hash << 5) - hash);
    const c = "00000" + (hash & 0x00FFFFFF).toString(16);
    return "#" + c.slice(c.length - 6);
}

class RecordingCamera {
    constructor(timeline, name){
        this.row = timeline.createRow();
        this.row.label.innerText = name;
        this.cells = new Map();
        this.id_counter = 0;
    }

    updateStarting() {
        this._pendingCells = new Map();
    }

    addEvent(event, timestamp) {
        if (event.type == "start") {
            this._pendingStartTime = timestamp;
            this._pendingStorage = event.storage;
        } else if (event.type == "stop") {
            this._handleChunk(timestamp);
        }
    }

    updateComplete() {
        for (let oldCell of this.cells)
            this.row.remove(oldCell[1]);
        this.cells = this._pendingCells;
        this._pendingCells = null;
    }

    _handleChunk(stopTime) {
        const startTime = this._pendingStartTime;
        const storage = this._pendingStorage;
        this._pendingStartTime = null;
        this._pendingStorage = null;
        const type = "record";
        let cell = this._findSimilar(startTime, stopTime, storage, type);
        if (cell != null) {
            this.cells.delete(cell.identifier);
        } else {
            cell = this.row.createPlain(startTime, stopTime, _colourForStorage(storage));
            cell.identifier = this.id_counter;
            cell.type = type;
            cell.storage = storage;
            this.id_counter++;
        }
        this._pendingCells.set(cell.identifier, cell);
    }

    _findSimilar(startTime, stopTime, storage, mode) {
        for (const event of this.cells) {
            const cell = event[1];
            if (cell.type != mode)
                continue;
            if (cell.storage != storage)
                continue;
            if (cell.endTime < startTime)
                continue;
            if (cell.startTime > stopTime)
                continue;
            return cell;
        }
        return null;
    }
}

class RecordingParser {
    constructor() {
        this.display = new TimeLine();
        this.cameras = new Map();
        this.scale = 60;
        this.display.control.onwheel = this._onwheel.bind(this);
    }

    updateData(storage) {
        let earliest = null;
        let latest = null;
        let seen = new Map();
        for (const cameraId in storage) {
            let camera;
            if (this.cameras.has(cameraId)) {
                camera = this.cameras.get(cameraId);
                this.cameras.delete(cameraId);
            } else {
                camera = new RecordingCamera(this.display, cameraId);
            }
            seen.set(cameraId, camera);
            camera.updateStarting();
            for (const event of storage[cameraId]) {
                let eventDate = new Date(event.timestamp);
                if ((earliest == null) || (earliest > eventDate))
                    earliest = eventDate;
                if ((latest == null) || (latest < eventDate))
                    latest = eventDate;
                camera.addEvent(event, eventDate);
            }
            camera.updateComplete();
        }
        this.startDate = earliest;
        this.endDate = latest;
        for (let oldRow of this.cameras)
            oldRow[1].row.remove();
        this.cameras = seen;
        this._reload(null);
    }

    zoomin() {
        this.scale /= 2;
        this._reload(0.5);
    }

    zoomout(){
        this.scale *= 2;
        this._reload(0.5);
    }

    _reload(scaleAround) {
        this.display.setScale(this.startDate, this.endDate, this.scale, 100, 10, scaleAround);
    }

    _onwheel(event) {
        var x = event.clientX - event.target.getBoundingClientRect().left;
        var move = 0;
        if (event.deltaY > 0)
            move = 2;
        else if (event.deltaY < 0)
            move = 0.5;
        if (move != 0) {
            this.scale *= move;
            this._reload(x / event.target.clientWidth);
        }
    }
}
