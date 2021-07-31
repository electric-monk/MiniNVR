class Session {
    constructor() {
        this.authtoken = "1234";
        this.discovery = new DataMonitor("/discovery", 1000);
        this.cameras = new DataMonitor("/allCameras", 1000);
        this.storage = new DataMonitor("/allStorage", 1000);
    }

    _getStorageReq(storage, camera, start, end, getVideo) {
        req = {};
        if (storage)
            req["storage"] = storage;
        if (camera)
            req["camera"] = camera;
        if (start)
            req["start"] = start;
        if (end)
            req["end"] = end;
        if (getVideo)
            req["video"] = getVideo;
        return req;
    }

    _loadMediaSource(path, sendbody = null) {
        return new MediaHelper(path, sendbody, {'X-Login-Token': this.authtoken});
    }

    searchStorage(storage, camera, start, end, callback) {
        exchangeJSON("/storage", (status, data) => callback((status == 200) ? data : null), this._getStorageReq(storage, camera, start, end, false));
    }

    getRecordingMediaSource(storage, camera, start, end) {
        return this._loadMediaSource("/storage", this._getStorageReq(storage, camera, start, end, true));
    }

    getLiveMediaSource(identifier) {
        return this._loadMediaSource("/stream-" + identifier);
    }
}

class Toolbar {
    constructor() {
        this.control = document.createElement("div");
        this.control.classList.add("toolbar");
    }

    attach(item) {
        this.control.appendChild(item.toolbarChild);
    }
}

class CameraMenuItem extends MenuItem {
    constructor(session, owner, identifier, info) {
        super(owner);
        this.session = session;
        this.identifier = identifier;
        this.info = info;
        this.control.innerHTML = info.title;
    }

    selected() {
        let win = new PlainWindow(400, 300);
        win.title.innerHTML = '<img src="/camera_2-1.png">&nbsp;' + this.info.title;
        let mediaSource = this.session.getLiveMediaSource(this.identifier);
        let video = mediaSource.createVideo();
        video.classList.add('cameraView');
        win.content.appendChild(video);
        document.body.appendChild(win.control);
        video.play();
    }
}

class CameraMenu {
    constructor(session) {
        this.menu = new Menu();
        this.menu.button.control.innerHTML = '<img src="/camera_2-0.png">';
        this.session = session;
        this.session.cameras.subscribe(data => this.update(data));

        this.toolbarChild = this.menu.control;
    }
    
    update(data) {
        scrubChildElements(this.menu.menu);
        for (let item in data)
            new CameraMenuItem(this.session, this.menu, item, data[item]);
    }
}

class ListAndDetailsFormItem extends ListAndDetailsItem {
    constructor(owner, identifier, divClass, settings, formdata, entry) {
        super(owner, identifier, divClass);
        this.settings = settings;
        this.form = new Form(formdata, entry);
        this.form.fields.save.onclick = (e) => this.save();
        if (this.form.fields.delcam) {
            this.form.fields.delcam.style = "background-color: red";
            this.form.fields.delcam.onclick = (e) => this.dodelete();
        }
    }

    reload(label) {
        const str = '<img style="float: left;" src="' + this.settings.icon + '">' + label + '<br>' + this.identifier;
        if (str != this.control.innerHTML)
            this.control.innerHTML = str;
    }

    selected() {
        super.selected();
        this.ladOwner.detail.appendChild(this.form.control);
    }

    save() {
        this.form.fieldset.setAttribute("disabled", "disabled");
        var data = this.form.capture();
        this.prepareSaveData(data);
        this.sendrequest(data);
    }

    dodelete() {
        this.sendrequest({
            'identifier': this.identifier,
            'delete': true
        });
    }

    sendrequest(data) {
        exchangeJSON(this.settings.url, (stat, dat) => this.savedone(stat, dat), data);
    }
    
    prepareSaveData(data) {
        data['identifier'] = this.identifier;
    }

    savedone(status, data) {
        if (status == 200) {
            scrubChildElements(this.ladOwner.detail);
            this.ladOwner.detail.innerHTML = "<center>" + this.settings.type + " updated!</center>";
        } else {
            this.form.fieldset.removeAttribute("disabled");
            alert(this.settings.type + " failed to update");
        }
    }
}

class CameraItem extends ListAndDetailsFormItem {
    constructor(owner, identifier, divClass, button, entry, extras=[]) {
        super(owner, identifier, divClass,
            {url: '/updateCamera', icon: '/camera_2-0.png', type: 'Camera'},
            [
                "General",
                ["Friendly name", "text", "title"],
                "Credentials",
                ["Username", "text", "username"],
                ["Password", "password", "password"],
                "Settings",
                ["Recording", "select", "record"],
                "&nbsp;",
                [button, "button", "save"]
            ].concat(extras),
            entry);
        this.checkStorage();
    }

    reload(label) {
        super.reload(label);
        this.selectStorage();
    }

    prepareSaveData(data) {
        super.prepareSaveData(data);
        data['endpoint'] = this.endpoint;
    }

    checkStorage() {
        this.ladOwner.constructStorage(this.form.fields.record);
        this.selectStorage();
    }
    
    selectStorage() {
        // Do nothing by default
    }
}

class CameraSettingsUnknown extends CameraItem {
    constructor(owner, identifier, entry) {
        super(owner, identifier, "itemUnknown", "Save", entry);
        this.datafields = {};
        const fields = [["Manufacturer", "Manufacturer"], ["Model", "Model"], ["IP Address", "address"], ["&nbsp;", ""]];
        for (var x of fields) {
            let tr = document.createElement("tr");
            this.form.control.container.insertBefore(tr, this.form.topentry);
            let td = document.createElement("td");
            tr.appendChild(td);
            td.setAttribute("align", "right");
            td.innerHTML = x[0];
            let td2 = document.createElement("td");
            tr.appendChild(td2);
            let td2body = document.createElement("b");
            td2.appendChild(td2body);
            if (x[1].length != 0)
                this.datafields[x[1]] = td2body;
        }
        this.update(entry);
    }

    update(entry) {
        if (!entry['Model'])
            return false;
        this.endpoint = entry['Endpoints'][0];
        this.reload(entry['Model']);
        for (let field in this.datafields)
            this.datafields[field].innerHTML = field == "address" ? this.identifier : entry[field];
        return true;
    }
}

class CameraSettingsKnown extends CameraItem {
    constructor(owner, identifier, entry) {
        super(owner, identifier, "itemKnown", "Update", entry, [["Delete", "button", "delcam"]]);
        this.insertAtEnd = false;
        this.update(entry);
    }

    update(entry) {
        if (!entry['title'])
            return false;
        this.endpoint = entry['endpoint'];
        this.storage = entry['record'];
        this.reload(entry['title']);
        return true;
    }

    selectStorage() {
        if ((this.form.fields.record.options.length > 1) && this.storage) {
            if (!this.loadedStorage) {
                this.loadedStorage = true;
                let count = 0;
                for (let item of this.form.fields.record.options) {
                    if (item.value == this.storage) {
                        this.form.fields.record.selectedIndex = count;
                        break;
                    }
                    count++;
                }
            }
        }
    }
}

class CameraSettings extends ListAndDetails {
    constructor(container, session) {
        super(container);
        this.session = session;
        this.cameras = [];
        this.found = [];
    }

    connect() {
        this.unsubscribeCameras = this.session.cameras.subscribe(data => this.updateCameras(data));
        this.unsubscribeDiscovery = this.session.discovery.subscribe(data => this.updateDiscovery(data));
        this.unsubscribeStorage = this.session.storage.subscribe(data => this.updateStorage(data));
    }

    disconnect() {
        this.unsubscribeCameras();
        this.unsubscribeCameras = null;
        this.unsubscribeDiscovery();
        this.unsubscribeDiscovery = null;
        this.unsubscribeStorage();
        this.unsubscribeStorage = null;
        this.cameras = [];
        this.found = [];
        this._update();
    }

    updateCameras(data) {
        this.cameras = data;
        this._update();
    }

    updateDiscovery(data) {
        this.found = data;
        this._update();
    }

    updateStorage(data) {
        this.storage = data;
        for (let i = 0; i < this.list.control.children.length; i++) {
            let page = this.list.control.children[i];
            page._object.checkStorage();
        }
    }

    _update() {
        let keys = [];
        for (let identifier in this.cameras)
            keys.push(identifier);
        for (let identifier in this.found)
            if (!keys.includes(identifier))
                keys.push(identifier);
        this.update(keys);
    }

    constructStorage(container) {
        if (container.options.length == 0) {
            let emptyOption = document.createElement("option");
            emptyOption.text = "-- Not recorded --";
            emptyOption.value = "";
            container.options.add(emptyOption);
        }
        let previous = {};
        for (let x of container.options)
            if (x.value != "")
                previous[x.value] = x;
        for (let x in this.storage) {
            if (previous[x]) {
                previous[x].text = this.storage[x].title;
                delete previous[x];
            } else {
                let newOption = document.createElement("option");
                newOption.text = this.storage[x].title;
                newOption.value = x;
                container.options.add(newOption);
            }
        }
        for (let x in previous)
            container.options.remove(previous[x]);
    }

    create(identifier) {
        if (this.cameras[identifier])
            return new CameraSettingsKnown(this, identifier, this.cameras[identifier]);
        if (this.found[identifier])
            return new CameraSettingsUnknown(this, identifier, this.found[identifier]);
        return null;
    }

    reload(object) {
        var entry = this.cameras[object.identifier];
        if (!entry)
            entry = this.found[object.identifier];
        return object.update(entry);
    }
}

class StorageSettingsItem extends ListAndDetailsFormItem {
    constructor(owner, identifier, divClass, buttonText, entry, extras=[]) {
        super(owner, identifier, divClass,
            {url: '/updateStorage', icon: '/backup_devices_2-0.png', type: 'Storage'},
            [
                "General",
                ["Friendly name", "text", "title"],
                "Settings",
                ["File location (server local disk)", "text", "filename"],
                ["Maximum size (bytes)", "number", "size"],
                "&nbsp;",
                [buttonText, "button", "save"]
            ].concat(extras),
            entry);
        this.update(entry);
    }

    update(entry) {
        this.reload(entry['title']);
        return true;
    }
}

class StorageSettingsKnown extends StorageSettingsItem {
    constructor(owner, identifier, entry) {
        super(owner, identifier, "itemKnown", "Update", entry, [["Delete", "button", "delcam"]]);
    }
}

class StorageSettingsUnknown extends StorageSettingsItem {
    constructor(owner) {
        super(owner, "&nbsp;", "itemUnknown", "Save", {'title': 'Create new storage'});
        this.form.fields.title.value = "";
    }

    prepareSaveData(data) {
        super.prepareSaveData(data);
        data['identifier'] = generateID();
    }

    savedone(status, data) {
        super.savedone(status, data);
        if (status == 200) {
            this.form.fields.title.value = "";
            this.form.fields.filename.value = "";
            this.form.fields.size.value = "";
        }
    }
}

class StorageSettings extends ListAndDetails {
    constructor(container, session) {
        super(container);
        this.session = session;
        this.storage = {};
        this.createItem = new StorageSettingsUnknown(this);
    }

    connect() {
        this.unsubscribeStorage = this.session.storage.subscribe(data => this.updateStorage(data));
    }

    disconnect() {
        this.unsubscribeStorage();
        this.unsubscribeStorage = null;
        this.storage = {};
        this._update();
    }

    reload(label) {
        const str = '<img style="float: left;" src="/camera_2-0.png">' + label + '<br>' + this.identifier;
        if (str != this.control.innerHTML)
            this.control.innerHTML = str;
    }

    updateStorage(data) {
        this.storage = data;
        this._update();
    }

    _update() {
        let keys = [this.createItem.identifier];
        for (let identifier in this.storage)
            keys.push(identifier);
        this.update(keys);
    }

    create(identifier) {
        if (identifier == this.createItem.identifier)
            return this.createItem;
        return new StorageSettingsKnown(this, identifier, this.storage[identifier]);
    }

    reload(object) {
        if (object.identifier == this.createItem.identifier)
            return true;
        return object.update(this.storage[object.identifier]);
    }
}

class UserSettings extends ListAndDetails {
    constructor(container, session) {
        super(container);
        this.session = session;
    }

    connect() {
    }

    disconnect() {
    }
}

class Settings extends PlainWindow {
    constructor(session) {
        super(400, 300, WIN_TITLE | WIN_MOVABLE | WIN_CLOSABLE | WIN_ACTIVATE | WIN_SIZABLE);
        this.title.innerHTML = "Settings";
        this.tabs = new Tabs();
        this.content.appendChild(this.tabs.control);
        this.parts = [];

        // Cameras
        var cameras = new TabItem(this.tabs, "Cameras");
        this.parts.push(new CameraSettings(cameras.content, session));

        // Storage
        var storage = new TabItem(this.tabs, "Storage");
        this.parts.push(new StorageSettings(storage.content, session));

        // Users
        var users = new TabItem(this.tabs, "Users");
        this.parts.push(new UserSettings(users.content, session));

        // About
        var about = new TabItem(this.tabs, "About");
        about.content.innerHTML = "<b>MiniNVR</b><br/>Version 0.1<br/>Copyright (C) 2021 Colin Munro<br/><br/><a href=\"https://github.com/electric-monk/MiniNVR\">https://github.com/electric-monk/MiniNVR</a><br/><br/>GNU General Public License v3.0";
    }

    open() {
        if (this.control.parentElement == null) {
            document.body.appendChild(this.control);
            for (let item of this.parts)
                item.connect();
        }
    }

    closed() {
        if (this.control.parentElement != null) {
            super.closed();
            for (let item of this.parts)
                item.disconnect();
        }
    }
}

class SettingsButton extends Button {
    constructor(session) {
        super(false);
        this.control.innerHTML = '<img src="/gears-0.png">';
        this.session = session;

        this.toolbarChild = this.control;
    }

    triggered() {
        if (!this.dialog)
            this.dialog = new Settings(this.session);
        this.dialog.open();
    }
}

function dologin() {
    var session = new Session();
    var toolbar = new Toolbar();
    document.body.appendChild(toolbar.control);
    toolbar.attach(new CameraMenu(session));
    toolbar.attach(new SettingsButton(session));
}
