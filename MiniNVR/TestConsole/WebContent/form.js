class Form {
    constructor(formdata, existingdata) {
        if (existingdata == null)
            existingdata = {};
        this.control = document.createElement("form");
        let fieldset = document.createElement("fieldset");
        this.control.appendChild(fieldset);
        fieldset.style.border = 0;
        let values = {};
        this.fields = values;
        this.fieldset = fieldset;
        {
            let table = document.createElement("table");
            fieldset.appendChild(table);
            this.control.container = table;
            for (let item of formdata) {
                let tr = document.createElement("tr");
                table.appendChild(tr);
                if (!this.topentry)
                    this.topentry = tr;
                let td = document.createElement("td");
                tr.appendChild(td);
                if (typeof (item) == "string") {
                    td.setAttribute("colspan", 2);
                    td.innerHTML = "<b>" + item + "</b>";
                } else {
                    let input = document.createElement("input");
                    input.setAttribute("type", item[1]);
                    if (existingdata[item[2]])
                        input.setAttribute("value", existingdata[item[2]]);
                    input.setAttribute("id", item[2]);
                    values[item[2]] = input;
                    let label = document.createElement("label");
                    label.setAttribute("for", item[2]);
                    label.innerHTML = item[0];
                    if (item[1] == "checkbox") {
                        td.setAttribute("colspan", 2);
                        td.appendChild(input);
                        td.appendChild(label);
                        input._readValue = () => input.checked;
                    } else if (item[1] == "button") {
                        td.setAttribute("colspan", 2);
                        td.setAttribute("align", "right");
                        input.setAttribute("value", item[0]);
                        td.appendChild(input);
                    } else if (item[1] == "select") {
                        let input = document.createElement("select");
                        input.setAttribute("id", item[2]);
                        values[item[2]] = input;
                        td.setAttribute("align", "right");
                        td.appendChild(label);
                        let td2 = document.createElement("td");
                        tr.appendChild(td2);
                        td2.appendChild(input);
                        input._readValue = () => input.options[input.selectedIndex].value;
                    } else {
                        td.setAttribute("align", "right");
                        td.appendChild(label);
                        let td2 = document.createElement("td");
                        tr.appendChild(td2);
                        td2.appendChild(input);
                        input._readValue = () => input.value;
                    }
                }
            }
        }
        this.control._object = this;
    }

    capture() {
        let result = {};
        for (let item in this.fields)
            if (this.fields[item]._readValue)
                result[item] = this.fields[item]._readValue();
        return result;
    }
}
