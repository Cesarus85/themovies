Compile check for the Java part of the player (Assets/Plugins/Android) without the Android SDK:
the stubs mirror the Android / Media3 1.5 signatures the plugin uses.

    javac -d /tmp/javacheck -sourcepath tools/JavaCheck/stubs Assets/Plugins/Android/*.java
