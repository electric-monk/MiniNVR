/* Windows.js/.css
 * Copyright (C) 2021 Colin Munro
 */

const WIN_SIZABLE = 1;
const WIN_MOVABLE = 2;
const WIN_TITLE = 4;
const WIN_CLOSABLE = 8;
const WIN_ACTIVATE = 16;

class PlainWindow {
    constructor(width, height, flags = WIN_SIZABLE | WIN_MOVABLE | WIN_TITLE | WIN_CLOSABLE | WIN_ACTIVATE) {
        var that = this;
        // Construct window frame
        this.control = document.createElement("div");
        this.control.classList.add("window");
        this.control.style.width = width + "px";
        this.control.style.height = height + "px";
        // Find window Z level, and deactivate all windows if necessary
        var allWindows = document.getElementsByClassName("window");
        var zLevel = allWindows.length;
        if (zLevel == 0)
            flags |= WIN_ACTIVATE;
        if (flags & WIN_ACTIVATE) {
            for (let other of allWindows)
                if ((other != this.control) && other._object.title)
                    other._object.title.classList.add("windowTitleInactive");
        } else {
            zLevel = 0;
            for (let other of allWindows)
                other.style.zIndex = other.style.zIndex + 1;
        }
        this.control.style.zIndex = zLevel;
        // Window position support
        var lastX = 0, lastY = 0;
        function dragMouseUp(e) {
            document.onmouseup = null;
            document.onmousemove = null;
            dragMouseMove(e);
        }
        function dragMouseMove(e) {
            e.preventDefault();
            var xOffset = lastX - e.clientX;
            var yOffset = lastY - e.clientY;
            lastX = e.clientX;
            lastY = e.clientY;
            that.control.style.top = (that.control.offsetTop - yOffset) + "px";
            that.control.style.left = (that.control.offsetLeft - xOffset) + "px";
        }
        function dragMouseDown(e) {
            e.preventDefault();
            document.onmouseup = dragMouseUp;
            document.onmousemove = dragMouseMove;
            lastX = e.clientX;
            lastY = e.clientY;
            that.activate();
        }
        // Window size support
        var sizeX = 0, sizeY = 0;
        function sizeMouseUp(e) {
            document.onmouseup = null;
            document.onmousemove = null;
            sizeMouseMove(e);
        }
        function sizeMouseMove(e) {
            e.preventDefault();
            var xOffset = sizeX - e.clientX;
            var yOffset = sizeY - e.clientY;
            sizeX = e.clientX;
            sizeY = e.clientY;
            let newX = (that.control.offsetWidth - xOffset);
            if (newX < 250)
                newX = 250;
            let newY = (that.control.offsetHeight - yOffset);
            if (newY < 250)
                newY = 250;
            that.control.style.height = newY + "px";
            that.control.style.width = newX + "px";
        }
        function sizeMouseDown(e) {
            e.preventDefault();
            document.onmouseup = sizeMouseUp;
            document.onmousemove = sizeMouseMove;
            sizeX = e.clientX;
            sizeY = e.clientY;
        }
        // Construct window doodahs
        if (flags & WIN_TITLE) {
            var title = document.createElement("div");
            this.titlebar = title;
            title.classList.add("windowTitle");
            this.control.appendChild(title);
            // Title area
            var text = document.createElement("div");
            text.style.float = "left";
            title.appendChild(text);
            this.title = text;
            // Close button
            if (flags & WIN_CLOSABLE) {
                var closeButton = document.createElement("div");
                closeButton.style.float = "right";
                closeButton.innerHTML = "<b>&#x2715;</b>";
                closeButton.onclick = function() {
                    that.closed();
                }
                title.appendChild(closeButton);
            }
            // Spacer
            var spacer = document.createElement("div");
            spacer.style = "clear: both;";
            title.appendChild(spacer);
            // Drag title
            if (flags & WIN_MOVABLE) {
                title.onmousedown = dragMouseDown;
            } else {
                title.onmousedown = function(e){
                    that.activate();
                };
            }
            if (!(flags & WIN_ACTIVATE))
                title.classList.add("windowTitleInactive");
        }
        this.content = document.createElement("div");
        this.content.classList.add("windowContent");
        this.control.appendChild(this.content);
        // Resize corner
        if (flags & WIN_SIZABLE) {
            var sizer = document.createElement("div");
            sizer.innerHTML = "&#x21d8;";
            sizer.style.fontSize = "200%";
            sizer.style.position = "absolute";
            sizer.style.right = 0;
            sizer.style.bottom = 0;
            sizer.style.border = "1px solid green;";
            this.control.appendChild(sizer);
            sizer.onmousedown = sizeMouseDown;
        }
        // Done
        this.control._object = this;
    }

    activate() {
        var allWindows = document.getElementsByClassName("window");
        var currentZLevel = this.control.style.zIndex;
        for (let other of allWindows) {
            if (other._object.title)
                other._object.title.classList.add("windowTitleInactive");
            if (other.style.zIndex >= currentZLevel)
                other.style.zIndex = other.style.zIndex - 1;
        }
        this.control.style.zIndex = allWindows.length - 1;
        this.title.classList.remove("windowTitleInactive");
    }

    closed() {
        this.control.parentElement.removeChild(this.control);
    }
}

function scrubChildElements(element) {
    while (element.firstChild)
        element.removeChild(element.firstChild);
}

class Button {
    constructor(toggle) {
        var that = this;
        this.control = document.createElement("div");
        this.control.classList.add("button");
        this.control.classList.add("buttonOff");
        this.state = false;
        function mouseUp(e) {
          if (!toggle)
              that.set(false);
          document.onmouseup = null;
          that.untriggered();
        }
        function mouseDown(e) {
          e.preventDefault();
          var set = !toggle || !that.state;
          document.onmouseup = mouseUp;
          that.set(set);
          that.triggered();
        }
        this.control.onmousedown = mouseDown;
        this.control._object = this;
    }

    set(state) {
        this.state = state;
        var remove, add;
        if (this.state) {
          remove = "buttonOff";
          add = "buttonOn";
        } else {
          remove = "buttonOn";
          add = "buttonOff";
        }
        this.control.classList.remove(remove);
        this.control.classList.add(add);
  }

  triggered() {
  }

  untriggered() {
  }
}

class MenuButton extends Button {
    constructor(owner) {
        super(true);
        this.owner = owner;
        this.active = false;
    }

    triggered() {
        if (this.state) {
            this.owner.control.appendChild(this.owner.menu);
            this.active = true;
        } else {
            this.owner.hide();
        }
    }
  
    untriggered() {
        if (this.active) {
            let that = this;
            document.onmousedown = function(e) {
                that.owner.hide();
            };
            this.active = false;
        }
    }
}

class Menu {
    constructor() {
        this.control = document.createElement("div");
        this.control.classList.add("menucontainer");
        this.menu = document.createElement("div");
        this.menu.classList.add("menu");
        this.button = new MenuButton(this);
        this.control.appendChild(this.button.control);
    }

    hide() {
        this.control.removeChild(this.menu);
        document.onmousedown = null;
        this.button.set(false);
    }
}

class MenuItem {
    constructor(owner) {
        this.control = document.createElement("div");
        this.control.onmousedown = (e) => this.selected();
        this.control.classList.add("menuitem");
        owner.menu.appendChild(this.control);
    }

    selected() {
    }
}

class Tabs {
    constructor() {
        this.control = document.createElement("div");
        this.control.classList.add("tabs");

        this.tabs = document.createElement("div");
        this.tabs.classList.add("tabBar");
        this.control.appendChild(this.tabs);

        this.spacer = document.createElement("div");
        this.spacer.classList.add("tabSpacer");
        this.spacer.innerHTML = "&nbsp;";
        this.tabs.appendChild(this.spacer);

        this.content = document.createElement("div");
        this.content.classList.add("tabPageContainer");
        this.control.appendChild(this.content);
    }

    focusItem(item) {
        for (var btn of this.tabs.childNodes) {
            btn.classList.remove("tabSelected");
            btn.classList.add("tabUnselected");
        }
        item.button.classList.add("tabSelected");
        item.button.classList.remove("tabUnselected");
        for (var page of this.content.childNodes)
            page.style.display = "none";
        item.content.style.display = null;
    }
}

class TabItem {
    constructor(owner, name) {
        this.button = document.createElement("div");
        this.button.classList.add("tabItem");
        var that = this;
        this.button.onmousedown = function(e) {
            owner.focusItem(that);
        };
        this.button.innerHTML = name;
        owner.tabs.insertBefore(this.button, owner.spacer);
        this.content = document.createElement("div");
        this.content.classList.add("tabPage");
        this.content.style.display = "none";
        owner.content.appendChild(this.content);
        if (owner.content.childNodes.length == 1)
            owner.focusItem(this);
    }
}

class ItemList {
    constructor() {
        this.control = document.createElement("div");
        this.control.classList.add("itemList");
    }

    reloadList(items) {
        scrubChildElements(this.control);
        for (let item of items)
            this.control.appendChild(item.control);0
    }
}

class ListItem {
    constructor(owner, identifier, divClass) {
        this.insertAtEnd = true;
        this.identifier = identifier;
        this.control = document.createElement("div");
        this.control.classList.add("itemUnselected");
        this.control.classList.add(divClass);
        this.control._object = this;
        let that = this;
        this.control.onmousedown = function (e) {
            for (let entry of owner.control.childNodes) {
                entry.classList.remove("itemSelected");
                entry.classList.add("itemUnselected");
            }
            that.selected();
        };
    }

    selected() {
        this.control.classList.remove("itemUnselected");
        this.control.classList.add("itemSelected");
    }
}

class ListAndDetailsItem extends ListItem {
    constructor(owner, identifier, divClass) {
        super(owner.list, identifier, divClass);
        this.ladOwner = owner;
    }

    selected() {
        super.selected();
        scrubChildElements(this.ladOwner.detail);
    }
}

class ListAndDetails {
    constructor(container) {
        this.list = new ItemList();
        container.appendChild(this.list.control);

        this.detail = document.createElement("div");
        this.detail.classList.add("itemSettings");
        container.appendChild(this.detail);
    }

    update(identifiers) {
        var previous = {};
        for (let i = 0; i < this.list.control.children.length; i++) {
            let item = this.list.control.children[i];
            previous[item._object.identifier] = item._object;
        }
        for (let identifier of identifiers) {
            let updated = false;
            if (previous[identifier]) {
                if (this.reload(previous[identifier])) {
                    updated = true;
                    delete previous[identifier];
                }
            }
            if (!updated) {
                let item = this.create(identifier);
                if (item) {
                    if (item.insertAtEnd)
                        this.list.control.appendChild(item.control);
                    else
                        this.list.control.insertBefore(item.control, this.list.control.firstChild);
                }
            }
        }
        for (let item in previous)
            this.list.control.removeChild(previous[item].control);
    }

    create(identifier) {
        return null;
    }

    reload(object) {
        return false;
    }
}
