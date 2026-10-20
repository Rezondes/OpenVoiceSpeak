// PasswordShow: turns the password field before the button into text and back, and says which state it is in.
function ovsShowPassword(button) {
    const input = button.previousElementSibling;
    const shown = input.type === "password";
    input.type = shown ? "text" : "password";
    button.setAttribute("aria-pressed", String(shown));
    button.textContent = shown ? button.dataset.hide : button.dataset.show;
}
