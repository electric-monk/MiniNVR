function zeroupstring(x) {
    y = "0" + x;
    return y.slice(y.length - 2);
}

function adjustcolour(colour, factor) {
    colour = colour.replace(/^\s*#|\s*$/g, '');
    if (colour.length == 3)
        colour = colour.replace(/(.)/g, "$1$1");
    let r = parseInt(colour.substr(0, 2), 16);
    let g = parseInt(colour.substr(2, 2), 16);
    let b = parseInt(colour.substr(4, 2), 16);
    r *= factor;
    g *= factor;
    b *= factor;
    if (r < 0) r = 0;
    if (g < 0) g = 0;
    if (b < 0) b = 0;
    if (r > 255) r = 255;
    if (g > 255) g = 255;
    if (b > 255) b = 255;
    return "#" +
        (0|(1<<8)+r).toString(16).slice(1) +
        (0|(1<<8)+g).toString(16).slice(1) +
        (0|(1<<8)+b).toString(16).slice(1);
}

class TimeLine_Cell {
    constructor(timeline, classname, label) {
        this.owner = timeline;
        this.control = document.createElement("div");
        this.control.className = classname;
        this.control.innerText = label;
        this.control._object = this;
        this.startTime = new Date();
    }

    _updateScale() {
        this.control.style.left = this.owner._computeOffset(this.startTime) + "px";
    }
}

class TimeLine_Cell_Themed extends TimeLine_Cell {
    constructor(timeline, classname, colour) {
        super(timeline, classname, "");
        let highlight = adjustcolour(colour, 1.3);
        let shadow = adjustcolour(colour, 0.7);
        let lineStyle = "1px solid ";
        this.control.style.background = colour;
        this.control.style.borderLeft = lineStyle + highlight;
        this.control.style.borderTop = lineStyle + highlight;
        this.control.style.borderRight = lineStyle + shadow;
        this.control.style.borderBottom = lineStyle + shadow;
        this.endTime = new Date();
    }

    _updateScale() {
        super._updateScale();
        this.control.style.width = (this.owner._computeOffset(this.endTime) - this.owner._computeOffset(this.startTime)) + "px";
    }
}

class TimeLine_POI extends TimeLine_Cell {
    constructor(timeline) {
        super(timeline, "TimeLine-POI", "&#x2664;");
    }
}

class TimeLine_Plain extends TimeLine_Cell_Themed {
    constructor(timeline, colour) {
        super(timeline, "TimeLine-Cell", colour);
    }
}

class TimeLine_POI_Time extends TimeLine_Cell_Themed {
    constructor(timeline, colour, index) {
        super(timeline, "TimeLine-Activity", colour);
        this.control.style.top = (2 + (index * 3)) + "px";
    }
}

class TimeLine_RulerCell extends TimeLine_Cell {
    constructor(timeline, label) {
        super(timeline, "TimeLine-RulerCell", label);
    }
}

class TimeLine_Row {
    constructor(timeline) {
        this.owner = timeline;

        this.control = document.createElement("div");
        this.control.className = "TimeLine-Row";

        this.label = document.createElement("div");
        this.label.className = "TimeLine-RowName";
        this.control.appendChild(this.label);

        this.ruler = document.createElement("div");
        this.ruler.className = "TimeLine-RowRuler";
        this.ruler.style.visibility = "hidden";
        this.control.appendChild(this.ruler);

        this.cells = document.createElement("div");
        this.cells.className = "TimeLine-RowCells";
        this.control.appendChild(this.cells);

        this.control._object = this;
        this.owner._scroller.appendChild(this.control);
    }

    remove() {
        this.owner._scroller.remove(this.control);
        this.owner = null;
        this.control._object = null;
    }

    createPOI(startTime) {
        let result = new TimeLine_POI(this.owner);
        result.startTime = startTime;
        return this._doAdd(result);
    }

    createPlain(startTime, endTime, colour) {
        let result = new TimeLine_Plain(this.owner, colour);
        result.startTime = startTime;
        result.endTime = endTime;
        return this._doAdd(result);
    }

    createTemporalPOI(startTime, endTime, colour, index) {
        let result = new TimeLine_POI_Time(this.owner, colour, index);
        result.startTime = startTime;
        result.endTime = endTime;
        return this._doAdd(result);
    }

    remove(cell) {
        this.cells.remove(cell.control);
    }

    _doAdd(cell) {
        cell._updateScale();
        this.cells.appendChild(cell.control);
        return cell;
    }

    _updateScale() {
        for (let cellDiv of this.cells.childNodes)
            cellDiv._object._updateScale();
    }

    _updateRuler() {
        if (this.owner._rulerOffset == null) {
            this.ruler.style.visibility = "hidden";
        } else {
            this.ruler.style.visibility = null;
            this.ruler.style.transform = "translateX(" + this.owner._rulerOffset + "px)";
        }
    }
}

class TimeLine_Ruler extends TimeLine_Row {
    constructor(timeline) {
        super(timeline);
        this.control.classList.add("TimeLine-Row--Top");
    }

    _updateScale() {
        // Don't call super here, as we remove/re-add items as the scale may require new labels
        this.cells.style.width = this.owner._computeOffset(this.owner.enddate) + "px";
        let smallTickSpace = this.owner.largeTickSpace / this.owner.smallTickDivider;
        this.cells.style.backgroundImage = "repeating-linear-gradient(90deg, #aaa, #aaa 1px, transparent 1px, transparent " + this.owner.largeTickSpace + "px), repeating-linear-gradient(90deg, #aaa, #aaa 1px, transparent 1px, transparent " + smallTickSpace + "px)";
        this.cells.innerText = "";
        let lastDay = -1;
        for (let curTime = this.owner.basedate.valueOf(); curTime < this.owner.enddate.valueOf(); curTime += this.owner.largeTickTime * 1000) {
            let dateObj = new Date(curTime);
            let curDay = dateObj.getDay();
            let label;
            if (curDay != lastDay) {
                let mon = dateObj.toLocaleString('default',{month:'short'});
                let day = zeroupstring(dateObj.getDate());
                label = day + mon;
                lastDay = curDay;
            } else {
                let hr = "" + dateObj.getHours();
                let min = zeroupstring(dateObj.getMinutes());
                label = hr + ":" + min;
            }
            let cell = new TimeLine_RulerCell(this.owner, label);
            cell.startTime = dateObj;
            cell._updateScale();
            this.cells.appendChild(cell.control);
        }
    }
}

class TimeLine {
    constructor() {
        this.control = document.createElement("div");
        this.control.className = "TimeLine";
        this.control._object = this;

        this._scroller = document.createElement("div");
        this._scroller.className = "TimeLine-Scroll";
        this.control.appendChild(this._scroller);

        this.ruler = new TimeLine_Ruler(this);
        this._scroller.appendChild(this.ruler.control);

        this._rulerTrueOffset = null;
        this._rulerOffset = null;

        this.setScale(new Date(), 60, 10, 1);
        this.ruler._updateScale();
    }

    createRow() {
        let row = new TimeLine_Row(this);
        row._updateScale();
        row._updateRuler();
        return row;
    }

    setScale(basedate, enddate, largeTickTime, largeTickSpace, smallTickDivider, scaleAround = 0.5) {
        let targetTime = null;
        if (scaleAround != null) {
            if (this.basedate != null)
                targetTime = this._computeTime(this.control.scrollLeft + (this.control.clientWidth * scaleAround));
            else
                targetTime = enddate;
        }
        let aligneddate = new Date(basedate.valueOf());
        if (aligneddate.getMinutes() > 30)
            aligneddate.setMinutes(30);
        else
            aligneddate.setMinutes(0);
        aligneddate.setSeconds(0);
        this.basedate = basedate;
        this.enddate = enddate;
        this.largeTickTime = largeTickTime;
        this.largeTickSpace = largeTickSpace;
        this.smallTickDivider = smallTickDivider;
        if (this._rulerTrueOffset != null)
            this._rulerOffset = this._computeOffset(this._rulerTrueOffset);
        for (let rowDiv of this._scroller.childNodes) {
            rowDiv._object._updateScale();
            if (this._rulerOffset != null)
                rowDiv._object._updateRuler();
        }
        if (targetTime != null)
            this.control.scrollLeft = this._computeOffset(targetTime) - (this.control.clientWidth * scaleAround);
    }

    showRuler(offset) {
        if (offset == null) {
            this._rulerOffset = null;
            this._rulerTrueOffset = null;
        } else {
            this._rulerOffset = this._computeOffset(offset);
            this._rulerTrueOffset = offset;
        }
        for (let rowDiv of this._scroller.childNodes)
            rowDiv._object._updateRuler();
    }

    _computeOffset(timeOffset) {
        let raw = (timeOffset.valueOf() - this.basedate.valueOf()) / 1000;
        return (raw * this.largeTickSpace) / this.largeTickTime;
    }

    _computeTime(pixelOffset) {
        let cooked = (pixelOffset * this.largeTickTime) / this.largeTickSpace;
        return new Date(this.basedate.valueOf() + (cooked * 1000));
    }
}
