function createButton(toggle) {
  var button = document.createElement("div");
  button.classList.add("button");
  button.classList.add("buttonOff");
  button._state = false;
  button._set = function(state) {
    button._state = state;
    if (button._state) {
      remove = "buttonOff";
      add = "buttonOn";
    } else {
      remove = "buttonOn";
      add = "buttonOff";
    }
    button.classList.remove(remove);
    button.classList.add(add);
  }
  function mouseUp(e) {
    if (!toggle)
      button._set(false);
    document.onmouseup = null;
    if (button._untriggered)
      button._untriggered();
  }
  function mouseDown(e) {
    e.preventDefault();
    var set = !toggle || !button._state;
    document.onmouseup = mouseUp;
    button._set(set);
    if (button._triggered)
      button._triggered(e.target);
  }
  button.onmousedown = mouseDown;
  return button;
}

function createMenu() {
  var container = document.createElement("div");
  container.classList.add("menucontainer");
  var menu = document.createElement("div");
  menu.classList.add("menu");
  menu._button = createButton(true);
  menu._hide = function() {
    container.removeChild(menu);
    document.onmousedown = null;
    menu._button._set(false);
  }
  function mouseDown(e) {
    menu._hide();
  }
  menu._button._triggered = function(t) {
    if (menu._button._state) {
      container.appendChild(menu);
      menu._button._untriggered = function() {
        document.onmousedown = mouseDown;
        menu._button._untriggered = null;
      };
    } else {
      menu._hide();
    }
  }
  menu._container = container;
  container.appendChild(menu._button);
  return menu;
}

function menuAddItem(menu, item) {
  var div = document.createElement("div");
  div.innerHTML = item;
  div.onmousedown = function() {
    if (div._selected)
      div._selected();
  };
  div.classList.add("menuitem");
  menu.appendChild(div);
  return div;
}

// Create bar along the bottom
function createToolbar() {
  var toolbar = document.createElement("div");
  toolbar.classList.add("toolbar");
  return toolbar;
}
