namespace Render.NET.Tgs
{
    internal static class TgsExpressionMath
    {
        internal const string Source = """
            function raw(a) { return a !== null && typeof a === 'object' && 'value' in a ? a.value : a; }
            function arithmetic(a,b,operation) {
                a=raw(a); b=raw(b);
                if(typeof a==='number' && typeof b==='number') return operation(a,b);
                if(Array.isArray(a) && Array.isArray(b)) return a.map((v,i)=>operation(v,b[i]));
                if(Array.isArray(a) && typeof b==='number') return a.map(v=>operation(v,b));
                if(typeof a==='number' && Array.isArray(b)) return b.map(v=>operation(a,v));
                throw new TypeError('Expression arithmetic requires numbers or vectors');
            }
            function $bm_mul(a,b) { return arithmetic(a,b,(x,y)=>x*y); }
            function $bm_div(a,b) { return arithmetic(a,b,(x,y)=>x/y); }
            function addSubtract(a,b,sign) {
                a=raw(a); b=raw(b);
                if(Array.isArray(a) && typeof b==='number') { var r=a.slice(); r[0]+=sign*b; return r; }
                if(typeof a==='number' && Array.isArray(b)) { var r=b.slice(); r[0]=a+sign*r[0]; return r; }
                return arithmetic(a,b,(x,y)=>x+sign*y);
            }
            function $bm_sum(a,b) {
                a=raw(a); b=raw(b);
                return typeof a==='string' || typeof b==='string' ? a+b : addSubtract(a,b,1);
            }
            function $bm_sub(a,b) { return addSubtract(a,b,-1); }
            function $bm_neg(a) { a=raw(a); return Array.isArray(a) ? a.map(x=>-x) : -a; }
            function $bm_isInstanceOfArray(a) { return Array.isArray(raw(a)); }
            function length(a,b) {
                a=arguments.length>1 ? $bm_sub(a,b) : raw(a);
                return Array.isArray(a) ? Math.sqrt(a.reduce((sum,x)=>sum+x*x,0)) : Math.abs(a);
            }
            function normalize(a) { return $bm_div(a,length(a)); }
            function clamp(a,low,high) { return Math.max(low,Math.min(high,a)); }
            function linear(t,a,b,x,y) {
                if(arguments.length===3) { x=a; y=b; a=0; b=1; }
                return $bm_sum(x,$bm_mul($bm_sub(y,x),clamp((t-a)/(b-a),0,1)));
            }
            function degreesToRadians(a) { return a*Math.PI/180; }
            function radiansToDegrees(a) { return a*180/Math.PI; }
            """;
    }
}
