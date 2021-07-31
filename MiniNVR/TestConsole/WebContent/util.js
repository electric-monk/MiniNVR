class Login extends PlainWindow {
    constructor() {
        super(1, 1, WIN_ACTIVATE | WIN_TITLE);
        this.title.innerHTML = "Login";
        this.control.style = "position: fixed; top: 50%; left: 50%; transform: translate(-50%, -50%);";
        this.form = new Form([
            ["Username", "text", "username"],
            ["Password", "password", "password"],
            "&nbsp;",
            ["Log in", "button", "login"]
        ], null);
        let that = this;
        this.form.fields.login.onclick = function(e) {
            that.done(that.form.capture());
        };
        this.content.append(this.form.content);
    }

    done(data) {
        this.closed();
    }
}
