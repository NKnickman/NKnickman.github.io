def define_env(env):
    """
    This is the hook for the functions (new form)
    """

    @env.macro
    def impl(gdscript, header, source):
        return """
=== "GDScript"

        ``` gdscript 
        """ + gdscript + """
        ```

=== "C++ Header"

        ``` cpp 
        """ + header + """
        ```

=== "C++ Source"

        ``` cpp 
        """ + source + """
        ```
            """